using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Services;

public sealed record MediaOutput(string Filename, BinaryData Data, string MimeType);

/// <summary>No application-level inline output cap. Transport/client/provider limits remain applicable.</summary>
public static class MediaOutputService
{
    public static async Task<CallToolResult> CreateAsync(IEnumerable<MediaOutput> outputs, bool delegated,
        Func<MediaOutput, CancellationToken, Task<ResourceLinkBlock?>> upload, CancellationToken cancellationToken = default)
    {
        var media = outputs.ToList();
        if (media.Count == 0 || media.Any(item => item.Data.Length == 0 || string.IsNullOrWhiteSpace(item.MimeType)))
            throw new InvalidOperationException("No valid media outputs were returned by the provider.");
        var content = new List<ContentBlock>();
        foreach (var item in media)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (delegated)
                content.Add(await upload(item, cancellationToken)
                    ?? throw new InvalidOperationException("The generated media could not be uploaded."));
            else if (item.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                content.Add(ImageContentBlock.FromBytes(item.Data.ToArray(), item.MimeType));
            else
                content.Add(new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(item.Data.ToMemory(),
                        uri: $"urn:mcp-output:{Guid.NewGuid():N}:{Uri.EscapeDataString(item.Filename)}", mimeType: item.MimeType)
                });
        }
        return new CallToolResult { Content = content };
    }
}
