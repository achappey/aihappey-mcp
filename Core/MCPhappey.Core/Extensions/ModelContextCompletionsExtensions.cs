using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static partial class ModelContextCompletionsExtensions
{
    public static CompletionsCapability? ToCompletionsCapability(this ServerConfig server,
        CompletionService completionService, Dictionary<string, string>? headers = null)
    {
        var hasComletion = completionService.CanComplete(server);

        return hasComletion ? new CompletionsCapability()
        {
    
        } : null;
    }

    public static async Task<CompleteResult?> ToCompleteResult(this RequestContext<CompleteRequestParams> requestContext,
         ServerConfig server,
      CompletionService completionService, Dictionary<string, string>? headers = null,
      CancellationToken cancellationToken = default)
    {
        requestContext.Services!.WithHeaders(headers);

        return await completionService.GetCompletion(requestContext.Params, server,
            requestContext.Services!, requestContext.Server, cancellationToken);
    }
}