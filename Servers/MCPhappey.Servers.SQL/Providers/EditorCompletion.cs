using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Servers.SQL.Extensions;

namespace MCPhappey.Servers.SQL.Providers;

public class EditorCompletion : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfig.Server.ServerInfo.Name.StartsWith("ModelContext-Editor")
            || serverConfig.Server.ServerInfo.Name.StartsWith("ModelContext-Security");

    public async Task<Completion> GetCompletion(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        CompleteRequestParams? completeRequestParams,
        CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();


        IEnumerable<string> values = [];
        switch (completeRequestParams?.Argument?.Name)
        {
            case "server":
                var servers = await serviceProvider.GetServers(cancellationToken);
                values = servers.Select(z => z.Name);
                break;
            case "resourceName":
            case "promptName":
                if (completeRequestParams.Context?.Arguments?.ContainsKey("server") == true)
                {
                    var serverName = completeRequestParams.Context?.Arguments["server"];

                    if (!string.IsNullOrEmpty(serverName))
                    {
                        var serverItem = await serviceProvider.GetServer(serverName, cancellationToken);

                        switch (completeRequestParams?.Argument?.Name)
                        {
                            case "resourceName":
                                values = serverItem.Resources
                                    .Select(z => z.Name);

                                break;
                            case "promptName":
                                values = serverItem.Prompts
                                    .Select(z => z.Name);

                                break;
                            default:
                                break;
                        }
                    }
                }

                break;

            default:
                var completionServices = serviceProvider.GetServices<IAutoCompletion>();
                var completionService = completionServices.FirstOrDefault(a => a.SupportsHost(new ServerConfig()
                {
                    Server = new Server()
                    {
                        ServerInfo = new ServerInfo()
                        {
                            Name = "Microsoft-"
                        }
                    }
                }));

                if (completionService == null) return new Completion();

                return await completionService.GetCompletion(mcpServer, serviceProvider, completeRequestParams, cancellationToken);
        }

        var filtered = values.Where(a => string.IsNullOrEmpty(argValue)
                                    || a.Contains(argValue, StringComparison.OrdinalIgnoreCase));

        return new Completion()
        {
            Values = [..filtered
                            .Order()
                            .Take(100)],
            HasMore = filtered.Count() > 100,
            Total = filtered.Count()
        };
    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
        => ["server", "resourceName", "promptName"];
}
