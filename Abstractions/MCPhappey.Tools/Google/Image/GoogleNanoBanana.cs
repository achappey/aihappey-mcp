using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
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
        [Description("Image model (gemini-2.5-flash-image or gemini-3-pro-image-preview)")]
        string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional image url for image edits. Supports protected links like SharePoint and OneDrive links")]
        string? fileUrl = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var googleAI = serviceProvider.GetRequiredService<Mscc.GenerativeAI.GoogleAI>();

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

        CreateMessageResult resultContent = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Messages = [..items?.Select(a => new ImageContentBlock() {
                    MimeType = a.MimeType,
                    Data = Convert.ToBase64String(a.Contents.ToArray())
                }.ToUserSamplingMessage()) ?? [],
                prompt.ToUserSamplingMessage()],
            IncludeContext = ContextInclusion.ThisServer,
            MaxTokens = 4096,
            SystemPrompt = "Create a single image according to the prompt",
            ModelPreferences = typed.Model?.ToModelPreferences(),
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>
                        {
                            { "google", new {

                            } },
                        })
        }, cancellationToken);

        return await requestContext.WithUploads(resultContent, serviceProvider, cancellationToken: cancellationToken);
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
        [Description("The image model. gemini-2.5-flash-image or gemini-3-pro-image-preview.")]
        public string Model { get; set; } = "gemini-3-pro-image-preview";
    }

}

