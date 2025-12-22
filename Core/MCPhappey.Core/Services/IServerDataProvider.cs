using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Core.Services;

public interface IServerDataProvider
{
    Task<IEnumerable<PromptTemplate>> GetPromptsAsync(string serverName, CancellationToken ct = default);
    Task<ListResourcesResult> GetResourcesAsync(string serverName, CancellationToken ct = default);
    Task<ListResourceTemplatesResult> GetResourceTemplatesAsync(string serverName, CancellationToken ct = default);
    Task<ServerConfig?> GetServer(string serverName, CancellationToken ct = default);
    Task<IEnumerable<ServerConfig?>> GetServers(CancellationToken ct = default);
}
