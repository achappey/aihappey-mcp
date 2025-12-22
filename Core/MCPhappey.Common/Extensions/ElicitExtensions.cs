using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class ElicitExtensions
{
    public static async Task<(T typedResult, CallToolResult? notAccepted, ElicitResult? elicitResult)> TryElicit<T>(
     this McpServer mcpServer,
     T elicitRequest,
     CancellationToken cancellationToken = default)
     where T : class, new()
    {
        var elicitParams = ElicitFormExtensions.CreateElicitRequestParamsForType<T>(elicitRequest);
        var result = await mcpServer.ElicitAsync(elicitParams, cancellationToken);
        if (result?.Action != "accept")
            throw new Exception($"Elicit not completed: {result?.Action}\n\n{JsonSerializer.Serialize(result, JsonSerializerOptions.Web)}");

        T typed = result?.GetTypedResult<T>() ?? throw new Exception("Type cast failed!");
        return (typed, null, result);
    }

    public static async Task<ElicitResult?> GetElicitResponse<T>(this McpServer mcpServer,
        string? message = null,
        CancellationToken cancellationToken = default) where T : new()
            => await mcpServer.ElicitAsync(
                    ElicitFormExtensions.CreateElicitRequestParamsForType<T>(default!, message),
                    cancellationToken: cancellationToken);
}
