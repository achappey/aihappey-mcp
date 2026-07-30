using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Image;

public static class GoogleNanoBanana
{
    [Description("Create a image with Google Nano Banana AI native image generator")]
    [McpServerTool(Title = "Generate image with Nano Banana", Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> GoogleNanoBanana_CreateImage(
        [Description("Image prompt (only English)")]
        string prompt,
        [Description("Image model (gemini-2.5-flash-image, gemini-3-pro-image, gemini-3.1-flash-image or gemini-3.1-flash-lite-image)")]
        string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional image url for image edits. Supports protected links like SharePoint and OneDrive links")]
        string? fileUrl = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
    {
        var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var items = !string.IsNullOrEmpty(fileUrl) ? await downloader.DownloadContentAsync(serviceProvider,
            requestContext.Server, fileUrl, cancellationToken) : null;

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
               new GoogleNanoBananaNewImage
               {
                   Prompt = prompt,
                   Model = model,
               },
               cancellationToken);

        var input = new JsonArray();
        foreach (var item in items ?? [])
            input.Add(GoogleInteractionInput.Bytes("image", item.Contents, item.MimeType));
        input.Add(GoogleInteractionInput.Text(typed.Prompt));

        var interaction = await interactions.CreateInteractionAsync(new GoogleInteractionRequest
        {
            Model = typed.Model,
            Input = input,
            SystemInstruction = "Create a single image according to the prompt.",
            ResponseFormat = new JsonObject { ["type"] = "image", ["mime_type"] = "image/png" },
            GenerationConfig = new JsonObject { ["max_output_tokens"] = 4096 }
        }, cancellationToken);

        return await interaction.ToToolResultAsync(requestContext, serviceProvider, cancellationToken);
    });


    [Description("Please fill in the AI image request details.")]
    public class GoogleNanoBananaNewImage
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("The image prompt. English prompts only")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("The image model. gemini-2.5-flash-image, gemini-3-pro-image, gemini-3.1-flash-image or gemini-3.1-flash-lite-image.")]
        public string Model { get; set; } = "gemini-3-pro";
    }

}

