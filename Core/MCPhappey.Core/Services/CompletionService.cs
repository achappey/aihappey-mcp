
using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Services;

public class CompletionService(
    IEnumerable<IAutoCompletion> autoCompletions)
{
    public bool CanComplete(ServerConfig serverConfig,
        CancellationToken cancellationToken = default) => autoCompletions.Any(z => z.SupportsHost(serverConfig));

    public async Task<CompleteResult> GetCompletion(CompleteRequestParams? completeRequestParams,
         ServerConfig serverConfig,
         IServiceProvider serviceProvider,
         McpServer mcpServer,
         CancellationToken cancellationToken = default)
    {
        var bestCompleter = autoCompletions
            .FirstOrDefault(a => a.SupportsHost(serverConfig));

        Completion? fileContent = null;
        if (bestCompleter != null)
        {
            fileContent = await bestCompleter.GetCompletion(mcpServer, serviceProvider, completeRequestParams, cancellationToken);
        }

        return new CompleteResult()
        {
            Completion = fileContent ?? new()
        };
    }
}
