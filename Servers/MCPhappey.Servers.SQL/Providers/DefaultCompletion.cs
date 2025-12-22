using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Auth.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Servers.SQL.Providers;

public class DefaultCompletion(IReadOnlyList<ServerConfig> serverConfigs) : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfigs.Any(a => a.Server.ServerInfo.Name == serverConfig.Server.ServerInfo.Name && serverConfig.SourceType == ServerSourceType.Dynamic)
         && serverConfig.Server.ServerInfo.Name.StartsWith("ModelContext-Editor") == false
            && serverConfig.Server.ServerInfo.Name.StartsWith("ModelContext-Security") == false;

    public async Task<Completion> GetCompletion(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        CompleteRequestParams? completeRequestParams,
        CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();

        IServerDataProvider sqlServerDataProvider = serviceProvider.GetRequiredService<IServerDataProvider>();
        ServerRepository serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var completionServices = serviceProvider.GetServices<IAutoCompletion>();

        var userId = serviceProvider.GetUserId();

        IEnumerable<string> values = [];
        var completionService = completionServices.First(a => a.SupportsHost(new ServerConfig()
        {
            Server = new Server()
            {
                ServerInfo = new ServerInfo()
                {
                    Name = "Microsoft-"
                }
            }
        }));

        var result = await completionService.GetCompletion(mcpServer, serviceProvider, completeRequestParams, cancellationToken);

        if (result.Values.Count > 0)
        {
            return result;
        }

        var simplicateCompletion = completionServices.First(a => a.SupportsHost(new ServerConfig()
        {
            Server = new Server()
            {
                ServerInfo = new ServerInfo()
                {
                    Name = "Simplicate-"
                }
            }
        }));

        return await simplicateCompletion.GetCompletion(mcpServer, serviceProvider, completeRequestParams, cancellationToken);
    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
    {
        var completionServices = serviceProvider.GetServices<IAutoCompletion>();
        List<string> items = [];

        foreach (var completion in completionServices)
        {
            if (ReferenceEquals(completion, this))
                continue;

            items.AddRange(completion.GetArguments(serviceProvider));
        }

        return items;
    }
}
