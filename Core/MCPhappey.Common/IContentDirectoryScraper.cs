using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Common;

public interface IContentDirectoryScraper
{
    bool SupportsDirectory(ServerConfig serverConfig, string url);

    Task<IReadOnlyDictionary<string, byte[]>> GetDirectoryAsync(McpServer? server,
        IServiceProvider services, string url, CancellationToken cancellationToken);
}
