using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Services;

public sealed record ImageOutput(string Filename, BinaryData Data, string MimeType);

/// <summary>Shared MCP output rendering; callers decide access from the effective server policy, not token shape.</summary>
public static class ImageOutputService
{
    public const int MaxInlineBytes = 10 * 1024 * 1024;

    public static async Task<CallToolResult> CreateAsync(IEnumerable<ImageOutput> outputs, bool delegated,
        Func<ImageOutput, CancellationToken, Task<ResourceLinkBlock?>> upload, CancellationToken cancellationToken = default)
    {
        var images = outputs.ToList();
        if (images.Count == 0 || images.Any(image => image.Data.Length == 0 || !image.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("No valid image outputs were returned by the provider.");
        if (!delegated && images.Sum(image => (long)image.Data.Length) > MaxInlineBytes)
            throw new InvalidOperationException("Generated images exceed the 10 MiB inline MCP output limit. Request a smaller image.");
        var blocks = new List<ContentBlock>();
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            blocks.Add(delegated
                ? await upload(image, cancellationToken) ?? throw new InvalidOperationException("The generated image could not be uploaded.")
                : ImageContentBlock.FromBytes(image.Data.ToArray(), image.MimeType));
        }
        return new CallToolResult { Content = blocks };
    }
}
