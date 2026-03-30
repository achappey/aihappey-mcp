using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Flexprice;

internal static class FlexpriceHelpers
{
    private const string DefaultBaseUrl = "https://us.api.flexprice.io/v1/";

    public static FlexpriceClient CreateClient(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext)
    {
        var headerProvider = serviceProvider.GetService<HeaderProvider>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);

        var http = httpClientFactory.CreateClient();
        var baseUrl = serverConfig?.Server?.McpExtension?.Url ?? DefaultBaseUrl;

        http.BaseAddress = new Uri(AppendTrailingSlash(baseUrl));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var apiKey = TryGetHeader(headerProvider?.Headers, "x-api-key")
            ?? TryGetHeader(serverConfig?.Server?.McpExtension?.Headers, "x-api-key");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new UnauthorizedAccessException("Flexprice x-api-key header is not configured.");

        http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
        return new FlexpriceClient(http);
    }

    public static async Task ConfirmExactNameAsync<TConfirm>(
        RequestContext<CallToolRequestParams> requestContext,
        string expectedName,
        CancellationToken cancellationToken = default)
        where TConfirm : class, IHasName, new()
    {
        var result = await requestContext.Server.GetElicitResponse<TConfirm>(expectedName, cancellationToken);

        if (result?.Action != "accept")
            throw new ValidationException($"Deletion confirmation was not accepted for '{expectedName}'.");

        var typed = result.GetTypedResult<TConfirm>()
            ?? throw new ValidationException("Deletion confirmation could not be parsed.");

        if (!string.Equals(typed.Name?.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Confirmation does not match '{expectedName}'.");
    }

    public static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void ValidateRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{name} is required.");
    }

    public static void ValidateAtLeastOne(params string?[] values)
    {
        if (values.All(string.IsNullOrWhiteSpace))
            throw new ValidationException("At least one update field is required.");
    }

    public static void ValidateRange(int? value, int min, int max, string name)
    {
        if (value is null)
            return;

        if (value < min || value > max)
            throw new ValidationException($"{name} must be between {min} and {max}.");
    }

    public static void ValidateCreditGrantScope(string scope, string? planId, string? subscriptionId)
    {
        if (scope.Equals("PLAN", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(planId))
            throw new ValidationException("planId is required when scope is PLAN.");

        if (scope.Equals("SUBSCRIPTION", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(subscriptionId))
            throw new ValidationException("subscriptionId is required when scope is SUBSCRIPTION.");
    }

    public static JsonObject? ParseJsonObject(string? raw, string parameterName)
    {
        var node = ParseJsonNode(raw, parameterName);
        if (node is null)
            return null;

        return node as JsonObject
            ?? throw new ValidationException($"{parameterName} must be a JSON object.");
    }

    public static JsonArray? ParseJsonArray(string? raw, string parameterName)
    {
        var node = ParseJsonNode(raw, parameterName);
        if (node is null)
            return null;

        return node as JsonArray
            ?? throw new ValidationException($"{parameterName} must be a JSON array.");
    }

    public static JsonNode? ParseJsonNode(string? raw, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (Exception ex)
        {
            throw new ValidationException($"{parameterName} must contain valid JSON. {ex.Message}");
        }
    }

    public static List<string>? SplitCsv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var values = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : values;
    }

    public static string EscapePath(string value)
        => Uri.EscapeDataString(value);

    public static FlexpriceToolResult<TResponse> CreateToolResult<TResponse>(
        string method,
        string endpoint,
        object? request,
        TResponse? response,
        string? resourceType = null,
        string? resourceId = null)
        => new()
        {
            Method = method,
            Endpoint = endpoint,
            Request = request,
            Response = response,
            ResourceType = resourceType,
            ResourceId = resourceId
        };

    private static string AppendTrailingSlash(string url)
        => url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";

    private static string? TryGetHeader(Dictionary<string, string>? headers, string name)
    {
        if (headers is null)
            return null;

        foreach (var kvp in headers)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value))
                return kvp.Value.Trim();
        }

        return null;
    }
}

internal sealed class FlexpriceToolResult<TResponse>
{
    public string Provider { get; set; } = "flexprice";

    public string Method { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public object? Request { get; set; }

    public TResponse? Response { get; set; }
}

internal sealed class FlexpriceEnvironmentResponse
{
    public string? CreatedAt { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Type { get; set; }

    public string? UpdatedAt { get; set; }
}

internal sealed class FlexpriceAddonResponse
{
    public string? CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public string? Description { get; set; }

    public JsonArray? Entitlements { get; set; }

    public string? EnvironmentId { get; set; }

    public string? Id { get; set; }

    public string? LookupKey { get; set; }

    public JsonObject? Metadata { get; set; }

    public string? Name { get; set; }

    public JsonArray? Prices { get; set; }

    public string? Status { get; set; }

    public string? TenantId { get; set; }

    public string? Type { get; set; }

    public string? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

internal sealed class FlexpriceCreditGrantResponse
{
    public string? Cadence { get; set; }

    public string? ConversionRate { get; set; }

    public string? CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public string? CreditGrantAnchor { get; set; }

    public string? Credits { get; set; }

    public string? EndDate { get; set; }

    public string? EnvironmentId { get; set; }

    public int? ExpirationDuration { get; set; }

    public string? ExpirationDurationUnit { get; set; }

    public string? ExpirationType { get; set; }

    public string? Id { get; set; }

    public JsonObject? Metadata { get; set; }

    public string? Name { get; set; }

    public string? Period { get; set; }

    public int? PeriodCount { get; set; }

    public string? PlanId { get; set; }

    public int? Priority { get; set; }

    public string? Scope { get; set; }

    public string? StartDate { get; set; }

    public string? Status { get; set; }

    public string? SubscriptionId { get; set; }

    public string? TenantId { get; set; }

    public string? TopupConversionRate { get; set; }

    public string? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}

internal sealed class FlexpriceListAddonsResponse
{
    public List<FlexpriceAddonResponse>? Items { get; set; }

    public FlexpricePaginationResponse? Pagination { get; set; }
}

internal sealed class FlexpriceListCreditGrantsResponse
{
    public List<FlexpriceCreditGrantResponse>? Items { get; set; }

    public FlexpricePaginationResponse? Pagination { get; set; }
}

internal sealed class FlexpricePaginationResponse
{
    public int? Limit { get; set; }

    public int? Offset { get; set; }

    public int? Total { get; set; }
}

internal sealed class FlexpriceSuccessResponse
{
    public string? Message { get; set; }
}

internal sealed class FlexpriceCreateEnvironmentRequest
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

internal sealed class FlexpriceUpdateEnvironmentRequest
{
    public string? Name { get; set; }

    public string? Type { get; set; }
}

internal sealed class FlexpriceCreateAddonRequest
{
    public string? Description { get; set; }

    public string LookupKey { get; set; } = string.Empty;

    public JsonObject? Metadata { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

internal sealed class FlexpriceUpdateAddonRequest
{
    public string? Description { get; set; }

    public JsonObject? Metadata { get; set; }

    public string? Name { get; set; }
}

internal sealed class FlexpriceAddonFilterRequest
{
    public List<string>? AddonIds { get; set; }

    public string? AddonType { get; set; }

    public string? EndTime { get; set; }

    public string? Expand { get; set; }

    public JsonArray? Filters { get; set; }

    public int? Limit { get; set; }

    public List<string>? LookupKeys { get; set; }

    public int? Offset { get; set; }

    public string? Order { get; set; }

    public JsonArray? Sort { get; set; }

    public string? StartTime { get; set; }

    public string? Status { get; set; }
}

internal sealed class FlexpriceCreateCreditGrantRequest
{
    public string Cadence { get; set; } = string.Empty;

    public string? ConversionRate { get; set; }

    public string Credits { get; set; } = string.Empty;

    public string? EndDate { get; set; }

    public int? ExpirationDuration { get; set; }

    public string? ExpirationDurationUnit { get; set; }

    public string? ExpirationType { get; set; }

    public JsonObject? Metadata { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Period { get; set; }

    public int? PeriodCount { get; set; }

    public string? PlanId { get; set; }

    public int? Priority { get; set; }

    public string Scope { get; set; } = string.Empty;

    public string? StartDate { get; set; }

    public string? SubscriptionId { get; set; }

    public string? TopupConversionRate { get; set; }
}

internal sealed class FlexpriceUpdateCreditGrantRequest
{
    public JsonObject? Metadata { get; set; }

    public string? Name { get; set; }
}

internal sealed class FlexpriceDeleteCreditGrantRequest
{
    public string? EffectiveDate { get; set; }
}
