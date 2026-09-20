using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class ElicitExtensions
{
    public static async Task<(Dictionary<string, JsonElement> values, ElicitResult? elicitResult)> TryElicitForm(
        this McpServer mcpServer,
        ElicitRequestParams elicitRequest,
        IReadOnlyDictionary<string, object?>? fallbackValues = null,
        CancellationToken cancellationToken = default)
    {
        if (mcpServer.ClientCapabilities?.Elicitation == null)
        {
            var values = fallbackValues?
                .Where(item => item.Value is not null)
                .ToDictionary(
                    item => item.Key,
                    item => JsonSerializer.SerializeToElement(item.Value, JsonSerializerOptions.Web))
                ?? [];

            return (values, null);
        }

        var result = await mcpServer.ElicitAsync(elicitRequest, cancellationToken);
        if (result?.Action != "accept")
            throw new Exception($"Elicit not completed: {result?.Action}\n\n{JsonSerializer.Serialize(result, JsonSerializerOptions.Web)}");

        return (result.Content?.ToDictionary() ?? [], result);
    }

    public static async Task<(T typedResult, CallToolResult? notAccepted, ElicitResult? elicitResult)> TryElicit<T>(
     this McpServer mcpServer,
     T elicitRequest,
     CancellationToken cancellationToken = default)
     where T : class, new()
        => await mcpServer.TryElicit(elicitRequest, propertyOverrides: null, cancellationToken);

    public static async Task<(T typedResult, CallToolResult? notAccepted, ElicitResult? elicitResult)> TryElicit<T>(
     this McpServer mcpServer,
     T elicitRequest,
     IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>? propertyOverrides,
     CancellationToken cancellationToken = default)
     where T : class, new()
    {
        if (mcpServer.ClientCapabilities?.Elicitation == null)
            return (elicitRequest, null, null);

        var elicitParams = ElicitFormExtensions.CreateElicitRequestParamsForType(elicitRequest, propertyOverrides);
        var result = await mcpServer.ElicitAsync(elicitParams, cancellationToken);
        if (result?.Action != "accept")
            throw new Exception($"Elicit not completed: {result?.Action}\n\n{JsonSerializer.Serialize(result, JsonSerializerOptions.Web)}");

        T typed = result?.GetTypedResult<T>() ?? throw new Exception("Type cast failed!");
        return (typed, null, result);
    }

    public static async Task<ElicitResult?> GetElicitResponse<T>(this McpServer mcpServer,
        string? message = null,
        CancellationToken cancellationToken = default) where T : new()
    {
        if (mcpServer.ClientCapabilities?.Elicitation == null)
            return null;

        return await mcpServer.ElicitAsync(
            ElicitFormExtensions.CreateElicitRequestParamsForType<T>(default!, message),
            cancellationToken: cancellationToken);
    }
}
