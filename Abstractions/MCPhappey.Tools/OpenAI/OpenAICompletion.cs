using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Auth.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.OpenAI;

public class OpenAICompletion(IReadOnlyList<ServerConfig> serverConfigs) : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfigs.Any(a => serverConfig.Server.ServerInfo.Name.StartsWith("OpenAI-") == true);

    public async Task<Completion> GetCompletion(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        CompleteRequestParams? completeRequestParams,
        CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();

        IServerDataProvider sqlServerDataProvider = serviceProvider.GetRequiredService<IServerDataProvider>();
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

        return new Completion();

    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
    {
        var completionServices = serviceProvider.GetServices<IAutoCompletion>();
        var userId = serviceProvider.GetUserId();

        if (string.IsNullOrEmpty(userId)) return [];

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

        return completionService.GetArguments(serviceProvider);
    }
}
