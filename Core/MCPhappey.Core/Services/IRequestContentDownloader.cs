using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services;

/// <summary>A server-specific credential boundary, evaluated before general configured scrapers.</summary>
public interface IRequestContentDownloader
{
    bool Supports(ServerConfig config);

    /// <summary>Return null to retain the existing delegated/general download pipeline.</summary>
    Task<IEnumerable<FileItem>?> DownloadAsync(IServiceProvider services, ServerConfig config,
        string url, CancellationToken cancellationToken = default);
}
