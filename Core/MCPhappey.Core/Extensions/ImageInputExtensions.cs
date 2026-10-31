using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class ImageInputExtensions
{
    public static async Task<FileItem> GetImageInputAsync(this IServiceProvider services, McpServer server,
        string url, CancellationToken cancellationToken = default)
    {
        if (services.GetEffectiveAccessMode(server) == ServerAccessMode.Delegated)
            return (await services.GetRequiredService<DownloadService>().DownloadContentAsync(services, server, url, cancellationToken))
                .FirstOrDefault() ?? throw new InvalidOperationException("The image could not be downloaded.");

        var uri = new Uri(url, UriKind.Absolute);
        var config = services.GetServerConfig(server)!;
        if (config.Server.OBO?.Keys.Any(key => uri.Host.Equals(key.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith('.' + key.TrimStart('.'), StringComparison.OrdinalIgnoreCase)) == true)
            throw new UnauthorizedAccessException("This image URL requires delegated access, which is not active.");
        return await services.GetRequiredService<PublicImageDownloader>().DownloadAsync(url, cancellationToken);
    }
}
