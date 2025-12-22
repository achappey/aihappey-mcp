using MCPhappey.Common.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common;

//
// Summary:
//     Interface for auto completion
public interface IAutoCompletion
{
    bool SupportsHost(ServerConfig serverConfig);

    Task<Completion> GetCompletion(McpServer mcpServer, IServiceProvider serviceProvider,
        CompleteRequestParams? completeRequestParams, CancellationToken cancellationToken = default);
    IEnumerable<string> GetArguments(IServiceProvider serviceProvider);

}