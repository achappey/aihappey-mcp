using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Image;

public static class OpenAIImageTools
{
    [Description("Ask OpenAI about multiple images.")]
    [McpServerTool(Title = "Ask multiple images", Name = "openai_ask_images", Destructive = false, OpenWorld = true, ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_AskImages(
        [Description("Prompt to execute.")] string prompt,
        [Description("Image URLs. SharePoint/OneDrive links are supported.")] List<string> imageUrls,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var response = await serviceProvider.GetRequiredService<OpenAIResponsesClient>().CreateResponseAsync(
            CreateVisionRequest(
                OpenAIResponsesClient.ResolveModel(model),
                prompt,
                await CreateImageInputAsync(serviceProvider, requestContext.Server, imageUrls, cancellationToken)),
            cancellationToken);

        return [(OpenAIResponsesClient.GetOutputText(response)
            ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output."))
            .ToTextContentBlock()];
    }

    [Description("Describes one or more images.")]
    [McpServerTool(Title = "Describe images", Name = "openai_describe_images", ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAI_DescribeImages(
        [Description("Image URLs to describe")] List<string> imageUrls,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Detail level")] ImageDescriptionDetailLevel imageDescriptionDetailLevel = ImageDescriptionDetailLevel.medium,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var prompt = await serviceProvider.GetRequiredService<PromptService>().GetServerPrompt(
                serviceProvider,
                requestContext.Server,
                "describe-images-in-detail",
                new Dictionary<string, JsonElement>
                {
                    ["detailLevel"] = JsonSerializer.SerializeToElement(imageDescriptionDetailLevel.GetEnumMemberValue())
                },
                cancellationToken: cancellationToken);

            var response = await serviceProvider.GetRequiredService<OpenAIResponsesClient>().CreateResponseAsync(
                CreateVisionRequest(
                    OpenAIResponsesClient.ResolveModel(model),
                    string.Join("\n\n", prompt.Messages.Select(message => message.Content.ToString())),
                    await CreateImageInputAsync(serviceProvider, requestContext.Server, imageUrls, cancellationToken)),
                cancellationToken);

            return (OpenAIResponsesClient.GetOutputText(response)
                ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output."))
                .ToTextCallToolResponse();
        });

    private static JsonObject CreateVisionRequest(string model, string prompt, JsonArray imageInput)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "input_text", ["text"] = prompt }
        };

        foreach (var image in imageInput)
            content.Add(image);

        return new JsonObject
        {
            ["model"] = model,
            ["reasoning"] = new JsonObject { ["effort"] = "low" },
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            }
        };
    }

    private static async Task<JsonArray> CreateImageInputAsync(
        IServiceProvider serviceProvider,
        McpServer server,
        IEnumerable<string> imageUrls,
        CancellationToken cancellationToken)
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var content = new JsonArray();

        foreach (var imageUrl in imageUrls)
        {
            var file = (await downloader.DownloadContentAsync(serviceProvider, server, imageUrl, cancellationToken))
                .FirstOrDefault();

            if (file?.Contents is null || string.IsNullOrWhiteSpace(file.MimeType))
                throw new InvalidOperationException($"Unable to download image content from '{imageUrl}'.");

            content.Add(new JsonObject
            {
                ["type"] = "input_image",
                ["image_url"] = $"data:{file.MimeType};base64,{Convert.ToBase64String(file.Contents.ToArray())}"
            });
        }

        return content;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ImageDescriptionDetailLevel
    {
        [Display(Name = "Low")]
        low,

        [Display(Name = "Medium")]
        medium,

        [Display(Name = "Detailed")]
        detailed
    }
}
