using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Valyu;

public static class ValyuApi
{
    [Description("Search across web and proprietary Valyu data sources via POST /v1/search.")]
    [McpServerTool(
        Name = "valyu_api_search",
        Title = "Valyu search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Valyu_Api_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required natural language search query.")] string query,
        [Description("Maximum number of results to return (1-20).")]
        int? maxNumResults = null,
        [Description("Search type: all, web, proprietary, or news.")]
        string? searchType = null,
        [Description("Maximum CPM budget.")]
        double? maxPrice = null,
        [Description("Minimum reranked relevance score between 0 and 1.")]
        double? relevanceThreshold = null,
        [Description("Included sources as JSON array string, e.g. [\"web\",\"valyu/valyu-arxiv\"].")]
        string? includedSourcesJson = null,
        [Description("Excluded sources as JSON array string.")]
        string? excludedSourcesJson = null,
        [Description("Source bias map as JSON object string, e.g. {\"nasa.gov\":5}.")]
        string? sourceBiasesJson = null,
        [Description("Custom instructions for query rewriting and reranking.")]
        string? instructions = null,
        [Description("Deprecated category fallback string.")]
        string? category = null,
        [Description("Whether the request originates from an AI tool call.")]
        bool? isToolCall = null,
        [Description("Response length: short, medium, large, max, or a positive integer as text.")]
        string? responseLength = null,
        [Description("Start date in YYYY-MM-DD format.")]
        string? startDate = null,
        [Description("End date in YYYY-MM-DD format.")]
        string? endDate = null,
        [Description("ISO 3166-1 alpha-2 country code, e.g. US, GB, NL, or ALL.")]
        string? countryCode = null,
        [Description("Enable fast mode for lower latency.")]
        bool? fastMode = null,
        [Description("Return URL-only web/news results without content extraction.")]
        bool? urlOnly = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumResults,
                    ["search_type"] = searchType,
                    ["max_price"] = maxPrice,
                    ["relevance_threshold"] = relevanceThreshold,
                    ["included_sources"] = ParseStringArrayOrNull(includedSourcesJson, nameof(includedSourcesJson)),
                    ["excluded_sources"] = ParseStringArrayOrNull(excludedSourcesJson, nameof(excludedSourcesJson)),
                    ["source_biases"] = ParseObjectOrNull(sourceBiasesJson, nameof(sourceBiasesJson)),
                    ["instructions"] = instructions,
                    ["category"] = category,
                    ["is_tool_call"] = isToolCall,
                    ["response_length"] = ParseResponseLengthOrNull(responseLength, nameof(responseLength)),
                    ["start_date"] = startDate,
                    ["end_date"] = endDate,
                    ["country_code"] = countryCode,
                    ["fast_mode"] = fastMode,
                    ["url_only"] = urlOnly
                };

                var client = serviceProvider.GetRequiredService<ValyuClient>();
                return await client.PostAsync("v1/search", payload, cancellationToken)
                    ?? throw new Exception("Valyu returned no response.");
            }));

    [Description("Extract clean structured content from URLs via POST /v1/contents.")]
    [McpServerTool(
        Name = "valyu_api_contents",
        Title = "Valyu contents",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Valyu_Api_Contents(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required URLs as JSON array string, e.g. [\"https://example.com\"].")]
        string urlsJson,
        [Description("Optional response mode or extraction mode string.")]
        string? mode = null,
        [Description("Optional response format string.")]
        string? format = null,
        [Description("Include AI summary when supported.")]
        bool? includeSummary = null,
        [Description("Include metadata when supported.")]
        bool? includeMetadata = null,
        [Description("Optional JSON schema as JSON object string for structured extraction.")]
        string? jsonSchema = null,
        [Description("Optional webhook URL for async callbacks.")]
        string? webhookUrl = null,
        [Description("Optional timeout in seconds.")]
        double? timeout = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var urls = ParseStringArrayOrNull(urlsJson, nameof(urlsJson));
                if (urls == null || urls.Count == 0)
                    throw new ArgumentException("urlsJson must contain at least one URL.", nameof(urlsJson));

                var payload = new JsonObject
                {
                    ["urls"] = urls,
                    ["mode"] = mode,
                    ["format"] = format,
                    ["include_summary"] = includeSummary,
                    ["include_metadata"] = includeMetadata,
                    ["json_schema"] = ParseObjectOrNull(jsonSchema, nameof(jsonSchema)),
                    ["webhook_url"] = webhookUrl,
                    ["timeout"] = timeout
                };

                var client = serviceProvider.GetRequiredService<ValyuClient>();
                return await client.PostAsync("v1/contents", payload, cancellationToken)
                    ?? throw new Exception("Valyu returned no response.");
            }));

    private static JsonObject? ParseObjectOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var node = JsonNode.Parse(value);
            return node as JsonObject;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }
    }

    private static JsonArray? ParseStringArrayOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var node = JsonNode.Parse(value);
            if (node is not JsonArray array)
                throw new ArgumentException($"{parameterName} must be a JSON array of strings.", parameterName);

            var result = new JsonArray();
            foreach (var item in array)
            {
                var text = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }
    }

    private static JsonNode? ParseResponseLengthOrNull(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var numericValue) && numericValue > 0)
            return JsonValue.Create(numericValue);

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "short" or "medium" or "large" or "max" => JsonValue.Create(normalized),
            _ => throw new ArgumentException($"{parameterName} must be short, medium, large, max, or a positive integer.", parameterName)
        };
    }
}
