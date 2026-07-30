using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
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
    {
        var content = new List<ContentBlock>();
        var text = GoogleInteractionResponse.GetText(interaction);
        if (!string.IsNullOrWhiteSpace(text))
            content.Add(new TextContentBlock { Text = text });

        foreach (var media in GoogleInteractionResponse.GetMedia(interaction, "image", "audio", "video", "document"))
        {
            if (media.Type.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                var upload = await requestContext.Server.Upload(
                    serviceProvider,
                    requestContext.ToOutputFileName(media.MimeType.ResolveExtensionFromMime()),
                    BinaryData.FromBytes(media.Data),
                    cancellationToken);
                if (upload is not null) content.Add(upload);
            }
            else if (media.Type.Equals("audio", StringComparison.OrdinalIgnoreCase))
            {
                content.Add(new AudioContentBlock { Data = media.Data, MimeType = media.MimeType });
            }
            else
            {
                var upload = await requestContext.Server.Upload(
                    serviceProvider,
                    requestContext.ToOutputFileName(media.MimeType.ResolveExtensionFromMime()),
                    BinaryData.FromBytes(media.Data),
                    cancellationToken);
                if (upload is not null) content.Add(upload);
            }
        }

        if (content.Count == 0)
            content.Add(new TextContentBlock { Text = interaction.ToJsonString() });

        return content.ToCallToolResponse();
    }
}
