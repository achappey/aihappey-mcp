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
        CancellationToken cancellationToken)
        => await CreateResultAsync(interaction, GoogleProviderAccess.IsHeaderMode(serviceProvider, requestContext.Server),
            (media, ct) => requestContext.Server.Upload(serviceProvider,
                requestContext.ToOutputFileName(media.MimeType.ResolveExtensionFromMime()),
                BinaryData.FromBytes(media.Data), ct), cancellationToken);

    internal static async Task<CallToolResult> CreateResultAsync(JsonObject interaction, bool headerMode,
        Func<GoogleInteractionMedia, CancellationToken, Task<ResourceLinkBlock?>> upload,
        CancellationToken cancellationToken = default)
    {
        var content = new List<ContentBlock>();
        var text = GoogleInteractionResponse.GetText(interaction);
        if (!string.IsNullOrWhiteSpace(text))
            content.Add(new TextContentBlock { Text = text });

        var mediaItems = GoogleInteractionResponse.GetMedia(interaction, "image", "audio", "video", "document");
        if (headerMode && mediaItems.Sum(media => (long)media.Data.Length) > ImageOutputService.MaxInlineBytes)
            throw new InvalidOperationException("Google media exceeds the 10 MiB inline MCP output limit. Request a smaller output.");

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
                content.Add(media.Data.ToBlobContent($"urn:google-output:{Guid.NewGuid():N}", media.MimeType));
            }
            else
            {
                content.Add(await upload(media, cancellationToken)
                    ?? throw new InvalidOperationException("The generated Google media could not be uploaded."));
            }
        }

        if (content.Count == 0)
            content.Add(new TextContentBlock { Text = interaction.ToJsonString() });

        return content.ToCallToolResponse();
    }
}
