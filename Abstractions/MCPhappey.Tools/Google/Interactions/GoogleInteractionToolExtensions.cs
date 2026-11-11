using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Interactions;

internal static class GoogleInteractionToolExtensions
{
    public static async Task<CallToolResult?> ToToolResultAsync(
        this JsonObject interaction,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken,
        string? requiredMediaType = null)
        => await CreateResultAsync(interaction, GoogleProviderAccess.IsHeaderMode(serviceProvider, requestContext.Server),
            (media, ct) => requestContext.Server.Upload(serviceProvider,
                requestContext.ToOutputFileName(media.MimeType.ResolveExtensionFromMime()),
                BinaryData.FromBytes(media.Data), ct), cancellationToken, requiredMediaType);

    internal static async Task<CallToolResult> CreateResultAsync(JsonObject interaction, bool headerMode,
        Func<GoogleInteractionMedia, CancellationToken, Task<ResourceLinkBlock?>> upload,
        CancellationToken cancellationToken = default,
        string? requiredMediaType = null)
    {
        var content = new List<ContentBlock>();
        var text = GoogleInteractionResponse.GetText(interaction);
        if (!string.IsNullOrWhiteSpace(text))
            content.Add(new TextContentBlock { Text = text });

        var mediaItems = GoogleInteractionResponse.GetMedia(interaction, "image", "audio", "video", "document");
        if (requiredMediaType is not null && !mediaItems.Any(media => media.Type.Equals(requiredMediaType, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Google returned no generated inline {requiredMediaType}. Provider inline-media, model, or regional restrictions may apply. No file-upload or stored-interaction fallback is used. " + text);
        var images = mediaItems.Where(media => media.Type.Equals("image", StringComparison.OrdinalIgnoreCase)).ToList();
        if (images.Count > 0)
        {
            var imageResult = await ImageOutputService.CreateAsync(images.Select(media =>
                    new ImageOutput("google-image" + media.MimeType.ResolveExtensionFromMime(), BinaryData.FromBytes(media.Data), media.MimeType)),
                !headerMode, (image, ct) => upload(new GoogleInteractionMedia("image", image.Data.ToArray(), image.MimeType), ct), cancellationToken);
            content.AddRange(imageResult.Content);
        }

        foreach (var media in mediaItems.Where(media => !media.Type.Equals("image", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (media.Type.Equals("audio", StringComparison.OrdinalIgnoreCase))
            {
                content.Add(new AudioContentBlock { Data = media.Data, MimeType = media.MimeType });
            }
            else if (headerMode)
            {
                content.Add(new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(media.Data, uri: $"urn:google-output:{Guid.NewGuid():N}", mimeType: media.MimeType)
                });
            }
            else
            {
                content.Add(await upload(media, cancellationToken)
                    ?? throw new InvalidOperationException("The generated Google media could not be uploaded."));
            }
        }

        if (content.Count == 0)
            throw new InvalidOperationException("Google returned no usable inline model output. Stored interactions and file-upload fallbacks are not supported by these tools.");

        return content.ToCallToolResponse();
    }
}
