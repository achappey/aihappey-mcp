using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Servers.SQL.Extensions;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Servers.SQL.Providers;

public class SqlServerDataProvider(Repositories.ServerRepository serverRepository, List<ServerIcon> defaultIcons) : IServerDataProvider
{
    public async Task<ServerConfig?> GetServer(string serverName, CancellationToken ct = default)
    {
        var server = await serverRepository.GetServer(serverName, ct)
            ?? throw new Exception("Not found");

        return new ServerConfig()
        {
            Server = server.ToMcpServer(defaultIcons),
            SourceType = ServerSourceType.Dynamic
        };
    }

    public async Task<IEnumerable<ServerConfig?>> GetServers(CancellationToken ct = default)
    {
        var servers = await serverRepository.GetServers(ct);

        return servers.Select(a => new ServerConfig()
        {
            Server = a.ToMcpServer(defaultIcons),
            SourceType = ServerSourceType.Dynamic
        });
    }

    public async Task<IEnumerable<PromptTemplate>> GetPromptsAsync(string serverName, CancellationToken ct = default)
    {
        var server = await serverRepository.GetServer(serverName, ct);

        return server?.Prompts.ToPromptTemplates().Prompts ?? [];
    }

    public async Task<ListResourcesResult> GetResourcesAsync(string serverName, CancellationToken ct = default)
    {
        var server = await serverRepository.GetResources(serverName, ct);

        return server?.ToListResourcesResult() ?? new ListResourcesResult();
    }

    public async Task<ListResourceTemplatesResult> GetResourceTemplatesAsync(string serverName, CancellationToken ct = default)
    {
        var server = await serverRepository.GetResourceTemplates(serverName, ct);

        return server?.ToListResourceTemplatesResult() ?? new ListResourceTemplatesResult();
    }
}
