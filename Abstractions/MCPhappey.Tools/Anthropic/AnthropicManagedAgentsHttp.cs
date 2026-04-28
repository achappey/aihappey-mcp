using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using MCPhappey.Tools.Anthropic.Skills;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic;

internal static class AnthropicManagedAgentsHttp
{
    internal const string ApiBaseUrl = AnthropicHeaders.ApiBaseUrl;
    internal const string AnthropicVersion = AnthropicHeaders.AnthropicVersion;

    internal static readonly string[] ManagedAgentsBetaFeatures =
    [
        AnthropicHeaders.ManagedAgentsBetaFeature
    ];

    internal static HttpClient CreateHttpClient(IServiceProvider serviceProvider, string? anthropicBetaCsv = null)
    {
        var settings = serviceProvider.GetRequiredService<AnthropicSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = clientFactory.CreateClient();

        httpClient.DefaultRequestHeaders.Add(AnthropicHeaders.ApiKeyHeader, settings.ApiKey);
        httpClient.DefaultRequestHeaders.Add(AnthropicHeaders.AnthropicVersionHeader, AnthropicVersion);
        httpClient.DefaultRequestHeaders.Add(AnthropicHeaders.AnthropicBetaHeader, BuildBetaHeader(anthropicBetaCsv));

        return httpClient;
    }

    internal static async Task<JsonNode> SendAsync(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string url,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        var httpClient = CreateHttpClient(serviceProvider);

        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{(int)response.StatusCode} {response.StatusCode}: {json}");

        return ParseJsonNode(json);
    }

    internal static async Task<JsonObject> GetJsonObjectAsync(
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken)
    {
        var node = await SendAsync(serviceProvider, HttpMethod.Get, url, null, cancellationToken);
        return node as JsonObject
               ?? throw new ValidationException($"Expected a JSON object from '{url}'.");
    }

    internal static async Task ConfirmDeleteAsync<TConfirm>(
        McpServer server,
        string expectedName,
        CancellationToken cancellationToken)
        where TConfirm : class, IHasName, new()
    {
        var dto = await server.GetElicitResponse<TConfirm>(expectedName, cancellationToken);

        if (dto?.Action != "accept")
            throw new ValidationException("Delete confirmation was not accepted.");

        var typed = dto.GetTypedResult<TConfirm>()
                    ?? throw new ValidationException("Delete confirmation response could not be parsed.");

        if (!string.Equals(typed.Name?.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Confirmation does not match name '{expectedName}'.");
    }

    internal static JsonArray CloneArray(JsonNode? node)
        => node?.DeepClone() as JsonArray ?? new JsonArray();

    internal static JsonObject CloneObject(JsonNode? node)
        => node?.DeepClone() as JsonObject ?? new JsonObject();

    internal static JsonNode BuildModelNode(string modelId, string? modelSpeed)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ValidationException("modelId is required.");

        if (string.IsNullOrWhiteSpace(modelSpeed))
            return JsonValue.Create(modelId)!;

        ValidateSpeed(modelSpeed);

        return new JsonObject
        {
            ["id"] = modelId,
            ["speed"] = modelSpeed
        };
    }

    internal static JsonObject? BuildPermissionPolicy(string? permissionPolicy)
    {
        if (string.IsNullOrWhiteSpace(permissionPolicy))
            return null;

        ValidatePermissionPolicy(permissionPolicy);
        return new JsonObject
        {
            ["type"] = permissionPolicy
        };
    }

    internal static void ValidatePermissionPolicy(string permissionPolicy)
    {
        if (!string.Equals(permissionPolicy, "always_allow", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(permissionPolicy, "always_ask", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("permissionPolicy must be 'always_allow' or 'always_ask'.");
        }
    }

    internal static void ValidateSpeed(string speed)
    {
        if (!string.Equals(speed, "standard", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(speed, "fast", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("modelSpeed must be 'standard' or 'fast'.");
        }
    }

    internal static JsonObject? ParseJsonObjectOrNull(string? json, string propertyName)
    {
        if (json is null)
            return null;

        if (string.IsNullOrWhiteSpace(json))
            return new JsonObject();

        var node = JsonNode.Parse(json);
        return node as JsonObject
               ?? throw new ValidationException($"{propertyName} must be a JSON object string.");
    }

    internal static JsonArray? ParseJsonArrayOrNull(string? json, string propertyName)
    {
        if (json is null)
            return null;

        if (string.IsNullOrWhiteSpace(json))
            return new JsonArray();

        var node = JsonNode.Parse(json);
        return node as JsonArray
               ?? throw new ValidationException($"{propertyName} must be a JSON array string.");
    }

    internal static List<string> ParseDelimited(string? value)
    {
        if (value is null)
            return [];

        return value
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);

        return array;
    }

    internal static int GetRequiredInt(JsonObject node, string propertyName)
        => node[propertyName]?.GetValue<int>()
           ?? throw new ValidationException($"Property '{propertyName}' is missing or invalid.");

    internal static string BuildBetaHeader(string? anthropicBetaCsv)
    {
        return AnthropicHeaders.BuildManagedAgentsBetaHeader(anthropicBetaCsv);
    }

    private static JsonNode ParseJsonNode(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JsonObject();

        return JsonNode.Parse(json)
               ?? throw new ValidationException("Anthropic response body could not be parsed as JSON.");
    }
}
