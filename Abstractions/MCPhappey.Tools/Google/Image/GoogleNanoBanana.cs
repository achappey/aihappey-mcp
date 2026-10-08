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
        [Description("Image model (gemini-nano-banana-2.1 or gemini-3.1-flash-lite-image)")]
        string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional image url for image edits. Supports protected links like SharePoint and OneDrive links")]
        string? fileUrl = null,
        [Description("Optional image aspect ratio, such as 1:1 for square, 16:9 for landscape, 9:16 for portrait, 4:3 for standard landscape, or 3:2 for photos. Defaults to the model's aspect ratio.")]
        string? aspectRatio = null,
        [Description("Optional image resolution: 1K (default), 2K, or 4K. Use 0.5K for 512px where supported. Gemini 3.1 Flash Lite Image supports only 1K. Values are case-sensitive.")]
        string? imageSize = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
    {
        var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var items = !string.IsNullOrEmpty(fileUrl) ? await downloader.DownloadContentAsync(serviceProvider,
            requestContext.Server, fileUrl, cancellationToken) : null;

        var typed = requestContext.Elicit(
               new GoogleNanoBananaNewImage
               {
                   Prompt = prompt,
                   Model = model,
                   AspectRatio = aspectRatio,
                   ImageSize = imageSize
               });

        var input = new JsonArray();
        foreach (var item in items ?? [])
            input.Add(GoogleInteractionInput.Bytes("image", item.Contents, item.MimeType));
        input.Add(GoogleInteractionInput.Text(typed.Prompt));

        var responseFormat = new JsonObject
        {
            ["type"] = "image",
            ["mime_type"] = "image/jpeg"
        };

        if (!string.IsNullOrWhiteSpace(typed.AspectRatio))
            responseFormat["aspect_ratio"] = typed.AspectRatio;

        if (!string.IsNullOrWhiteSpace(typed.ImageSize))
            responseFormat["image_size"] = typed.ImageSize;

        var interaction = await interactions.CreateInteractionAsync(
            new GoogleInteractionRequest
            {
                Model = typed.Model,
                Input = input,
                SystemInstruction = "Create a single image according to the prompt.",
                ResponseFormat = responseFormat
            },
            cancellationToken);

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
        [Description("The image model. gemini-nano-banana-2.1 or gemini-3.1-flash-lite-image.")]
        public string Model { get; set; } = "gemini-nano-banana-2.1";

        [JsonPropertyName("aspect_ratio")]
        [Description("Optional aspect ratio. Common values: 1:1 (square), 16:9 (landscape), 9:16 (portrait), 4:3 or 3:2. Leave empty for model default.")]
        public string? AspectRatio { get; set; }

        [JsonPropertyName("image_size")]
        [Description("Optional resolution: 1K, 2K, 4K or 0.5K where supported. Flash Lite supports only 1K. Values are case-sensitive.")]
        public string? ImageSize { get; set; }
    }

}

