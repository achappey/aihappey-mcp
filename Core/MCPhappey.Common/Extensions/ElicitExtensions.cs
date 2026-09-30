using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Common.Extensions;

public static class ElicitExtensions
{
    public static Task<(
    Dictionary<string, JsonElement> values,
    ElicitResult? elicitResult)> TryElicitForm(
    this RequestContext<CallToolRequestParams> requestContext,
    ElicitRequestParams elicitRequest,
    IReadOnlyDictionary<string, object?>? fallbackValues = null,
    CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string inputKey = "elicitForm";

        if (requestContext.Params?.InputResponses?
            .TryGetValue(inputKey, out var inputResponse) is true)
        {
            var result = inputResponse.Deserialize(
                InputResponse.ElicitResultJsonTypeInfo)
                ?? throw new InvalidOperationException(
                    "Invalid elicitation response.");

            if (result.Action != "accept")
                throw new InvalidOperationException(
                    $"Elicitation not accepted: {result.Action}");

            return Task.FromResult((
                values: result.Content?.ToDictionary() ?? [],
                elicitResult: (ElicitResult?)result));
        }

        if (!requestContext.Server.IsMrtrSupported)
        {
            var values = fallbackValues?
                .Where(item => item.Value is not null)
                .ToDictionary(
                    item => item.Key,
                    item => JsonSerializer.SerializeToElement(
                        item.Value,
                        JsonSerializerOptions.Web))
                ?? [];

            return Task.FromResult((
                values,
                (ElicitResult?)null));
        }

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [inputKey] = InputRequest.ForElicitation(elicitRequest)
            },
            requestState: $"elicit:{inputKey}");
    }

    public static T Elicit<T>(
        this RequestContext<CallToolRequestParams> requestContext,
        T fallbackValue,
        IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>? propertyOverrides = null,
        string? message = null)
        where T : class, new()
    {
        var inputKey = char.ToLowerInvariant(typeof(T).Name[0]) + typeof(T).Name[1..];

        if (requestContext.Params?.InputResponses?
            .TryGetValue(inputKey, out var inputResponse) is true)
        {
            var result = inputResponse.Deserialize(InputResponse.ElicitResultJsonTypeInfo)
                ?? throw new InvalidOperationException("Invalid elicitation response.");

            if (result.Action != "accept")
                throw new InvalidOperationException(
                    $"Elicitation not accepted: {result.Action}");

            return result.GetTypedResult<T>()
                ?? throw new InvalidOperationException("Elicitation result type cast failed.");
        }

        if (!requestContext.Server.IsMrtrSupported)
            return fallbackValue;

        var elicitRequest =
            ElicitFormExtensions.CreateElicitRequestParamsForType(
                fallbackValue,
                propertyOverrides,
                message);

        throw new InputRequiredException(
            inputRequests: new Dictionary<string, InputRequest>
            {
                [inputKey] = InputRequest.ForElicitation(elicitRequest)
            },
            requestState: $"elicit:{inputKey}");
    }

    [Obsolete("Use requestContext.Elicit(...) for new code.")]
    public static Task<(
       T typedResult,
       CallToolResult? notAccepted,
       ElicitResult? elicitResult)> TryElicit<T>(
       this RequestContext<CallToolRequestParams> requestContext,
       T elicitRequest,
       CancellationToken cancellationToken = default)
       where T : class, new()
    {
        return requestContext.TryElicit(
            elicitRequest,
            propertyOverrides: null,
            cancellationToken);
    }

    [Obsolete("Use requestContext.Elicit(...) for new code.")]
    public static Task<(
        T typedResult,
        CallToolResult? notAccepted,
        ElicitResult? elicitResult)> TryElicit<T>(
        this RequestContext<CallToolRequestParams> requestContext,
        T elicitRequest,
        IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>? propertyOverrides,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        var typed = requestContext.Elicit(
            elicitRequest,
            propertyOverrides);

        return Task.FromResult((
            typedResult: typed,
            notAccepted: (CallToolResult?)null,
            elicitResult: (ElicitResult?)null));
    }

}
