using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WebCrawlerAPI;

internal static class WebCrawlerAPIHelpers
{
    public const int DefaultPollIntervalSeconds = 2;
    public const int DefaultMaxWaitSeconds = 600;

    public static JsonArray ParseCsvArray(string? csv, string defaultCsv, string fieldName)
    {
        var values = (string.IsNullOrWhiteSpace(csv) ? defaultCsv : csv)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => (JsonNode?)JsonValue.Create(x))
            .ToArray();

        if (values.Length == 0)
            throw new ValidationException($"{fieldName} must contain at least one value.");

        return [.. values];
    }

    public static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{parameterName} is required.");
    }

    public static JsonObject WithoutNulls(this JsonObject obj)
    {
        foreach (var property in obj.ToList())
        {
            if (property.Value is null)
            {
                obj.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject child)
            {
                child.WithoutNulls();
                if (child.Count == 0)
                    obj.Remove(property.Key);
            }
        }

        return obj;
    }

    public static async Task<JsonNode> PollJobUntilTerminalAsync(
        WebCrawlerAPIClient client,
        string jobId,
        CancellationToken cancellationToken)
    {
        ValidateRequired(jobId, nameof(jobId));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultMaxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            var job = await client.GetJsonAsync($"v1/job/{Uri.EscapeDataString(jobId)}", timeoutCts.Token);
            var status = job?["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();

            if (status is "done" or "error")
                return job ?? new JsonObject();

            await Task.Delay(TimeSpan.FromSeconds(DefaultPollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"WebCrawlerAPI job {jobId} did not reach done or error within {DefaultMaxWaitSeconds} seconds.");
    }

    public static CallToolResult CreateToolResult(
        RequestContext<CallToolRequestParams> requestContext,
        JsonObject structured,
        string summary)
        => new()
        {
            Meta = requestContext.GetToolMeta().GetAwaiter().GetResult(),
            StructuredContent = structured.ToJsonElement(),
            Content = [summary.ToTextContentBlock()]
        };

    public static async Task<CallToolResult> CreateToolResultAsync(
        RequestContext<CallToolRequestParams> requestContext,
        JsonObject structured,
        string summary)
        => new()
        {
            Meta = await requestContext.GetToolMeta(),
            StructuredContent = structured.ToJsonElement(),
            Content = [summary.ToTextContentBlock()]
        };

    public static JsonObject CreateStructuredResponse(string endpoint, JsonNode? request, JsonNode? response)
        => new JsonObject
        {
            ["provider"] = "webcrawlerapi",
            ["baseUrl"] = WebCrawlerAPIClient.BaseUrl,
            ["endpoint"] = endpoint,
            ["request"] = request?.DeepClone(),
            ["response"] = response?.DeepClone()
        }.WithoutNulls();

    public static JsonNode? TryParseJson(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"{fieldName} must be valid JSON.", ex);
        }
    }
}

