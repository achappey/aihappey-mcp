using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Image;

public static class GoogleNanoBanana
{
    [Description("Generate or edit an image with Google Images using explicitly supplied references; no conversation is stored.")]
    [McpServerTool(Title = "Google Images: generate or edit image", Destructive = false, ReadOnly = false, Idempotent = false, OpenWorld = true)]
    public static async Task<CallToolResult?> GoogleNanoBanana_CreateImage(
        [Description("Image prompt (only English)")]
        string prompt,
        [Description("Image model (gemini-nano-banana-2.1 or gemini-3.1-flash-lite-image)")]
        string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional public HTTPS image URL for edits. Protected SharePoint and OneDrive links require delegated authentication.")]
        string? fileUrl = null,
        [Description("Optional image aspect ratio, such as 1:1 for square, 16:9 for landscape, 9:16 for portrait, 4:3 for standard landscape, or 3:2 for photos. Defaults to the model's aspect ratio.")]
        string? aspectRatio = null,
        [Description("Optional image resolution: 1K (default), 2K, or 4K. Use 0.5K for 512px where supported. Gemini 3.1 Flash Lite Image supports only 1K. Values are case-sensitive.")]
        string? imageSize = null,
        [Description("Additional reference image URLs for combining images or keeping subjects consistent. Supplied in order after fileUrl. Protected Microsoft links require delegated access.")]
        string[]? referenceImageUrls = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
    {
        var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        var urls = (string.IsNullOrWhiteSpace(fileUrl) ? Array.Empty<string>() : new[] { fileUrl })
            .Concat(referenceImageUrls ?? []).ToArray();

        var typed = requestContext.Elicit(
               new GoogleNanoBananaNewImage
               {
                   Prompt = prompt,
                   Model = model,
                   AspectRatio = aspectRatio,
                   ImageSize = imageSize
               });

        var input = new JsonArray();
        foreach (var url in urls)
        {
            var item = await serviceProvider.GetMediaInputAsync(requestContext.Server, url, "image", cancellationToken);
            input.Add(GoogleInteractionInput.Bytes("image", item.Contents, item.MimeType));
        }
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
            cancellationToken, requestContext.Server);

        return await interaction.ToToolResultAsync(requestContext, serviceProvider, cancellationToken, requiredMediaType: "image");
    });

    [Description("Answer a question about supplied image URLs with a Google vision model. Each call is self-contained and no interaction is stored.")]
    [McpServerTool(Title = "Google Images: ask about images", Destructive = false, ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> GoogleNanoBanana_AskImages(
        [Description("Question to answer about the images.")] string question,
        [Description("Image URLs in reference order. Public HTTPS URLs or protected Microsoft links in delegated mode.")] string[] imageUrls,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Vision-capable understanding model, not an image-generation model.")] string model = GoogleInteractionsClient.DefaultModel,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            ArgumentNullException.ThrowIfNull(imageUrls);
            if (imageUrls.Length == 0) throw new ArgumentException("At least one image URL is required.");
            var input = new JsonArray();
            foreach (var url in imageUrls)
            {
                var image = await serviceProvider.GetMediaInputAsync(requestContext.Server, url, "image", cancellationToken);
                input.Add(GoogleInteractionInput.Bytes("image", image.Contents, image.MimeType));
            }
            input.Add(GoogleInteractionInput.Text(question));
            var text = await serviceProvider.GetRequiredService<GoogleInteractionsClient>().CreateTextInteractionAsync(
                new GoogleInteractionRequest { Model = model, Input = input }, cancellationToken, requestContext.Server);
            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
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

