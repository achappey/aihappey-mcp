using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.Rijksoverheid;

public sealed class OmgevingswetContentDownloader : IRequestContentDownloader
{
    public bool Supports(ServerConfig config) => OmgevingswetClient.Supports(config);

    public async Task<IEnumerable<FileItem>?> DownloadAsync(IServiceProvider services, ServerConfig config,
        string url, CancellationToken cancellationToken = default)
        => [await services.GetRequiredService<OmgevingswetClient>().GetAsync(services, config, url, cancellationToken)];
}
