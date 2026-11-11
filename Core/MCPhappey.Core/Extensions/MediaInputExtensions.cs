using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

/// <summary>Self-contained media inputs. Header access never reaches configured scrapers or Graph.</summary>
public static class MediaInputExtensions
{
    public static async Task<FileItem> GetMediaInputAsync(this IServiceProvider services, McpServer server,
        string url, string kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var mode = services.GetEffectiveAccessMode(server);
        FileItem file;
        if (kind == "image")
            file = await services.GetImageInputAsync(server, url, cancellationToken);
        else if (mode == ServerAccessMode.Delegated)
            file = (await services.GetRequiredService<DownloadService>()
                .DownloadContentAsync(services, server, url, cancellationToken)).FirstOrDefault()
                ?? throw new InvalidOperationException("The media could not be downloaded.");
        else if (mode == ServerAccessMode.Headers)
        {
            var uri = new Uri(url, UriKind.Absolute);
            var config = services.GetServerConfig(server)!;
            if (config.Server.OBO?.Keys.Any(host => uri.Host.Equals(host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith('.' + host.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) == true)
                throw new UnauthorizedAccessException("This media URL requires delegated access.");
            // Videos/artifacts can legitimately be larger than the generic document-input limit.
            file = await services.GetRequiredService<PublicFileDownloader>().DownloadAsync(url, cancellationToken, maxBytes: null);
        }
        else
            throw new UnauthorizedAccessException("Provider headers or delegated authentication are required.");

        if (file.Contents.Length == 0)
            throw new InvalidOperationException("The input media is empty.");
        if (kind == "image")
            PublicImageDownloader.ValidateImage(file.Contents.ToArray(), file.MimeType);
        else if (kind == "video" && !file.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The input URL must return video content.");
        return file;
    }
}
