using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WebsearchAPI;

public static class WebsearchAPIService
{
    private const string BaseUrl = "https://api.websearchapi.ai";

    [Description("Search the web using WebSearchAPI.ai POST /ai-search and return structured search results.")]
    [McpServerTool(Title = "WebsearchAPI AI Search", Name = "websearchapi_search", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> WebsearchAPI_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query.")] string query,
        [Description("Maximum number of organic results.")] int? maxResults = null,
        [Description("Include extracted page content.")] bool? includeContent = null,
        [Description("Extracted content length hint (short, medium, large).")]
        string? contentLength = null,
        [Description("Output format for extracted content (markdown, text, html).")]
        string? contentFormat = null,
        [Description("Country code for localized search results (e.g., us, nl, de).")]
        string? country = null,
        [Description("Language code for localized search results (e.g., en, nl, de).")]
        string? language = null,
        [Description("Time filter (day, week, month, year).")]
        string? timeframe = null,
        [Description("Include generated answer in response.")] bool? includeAnswer = null,
        [Description("Enable safe search filtering.")] bool? safeSearch = null,
        [Description("Comma-separated domains to include (e.g., example.com,contoso.com).")]
        string? includeDomainsCsv = null,
        [Description("Comma-separated domains to exclude (e.g., spam.com,ads.example).")]
        string? excludeDomainsCsv = null,
        [Description("Restrict search to a single site/domain.")] string? siteSearch = null,
        [Description("Exact phrase that must appear in results.")] string? exactTerms = null,
        [Description("Exclude pages containing these terms.")] string? excludeTerms = null,
        [Description("Restrict by file type (e.g., pdf, docx, pptx).")]
        string? fileType = null,
        [Description("Optional raw JSON object string merged into request body for advanced parameters.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await PostEndpoint(
            serviceProvider,
            requestContext,
            endpoint: "/ai-search",
            payloadBuilder: payload =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                payload["query"] = query;
                SetIfHasValue(payload, "maxResults", maxResults);
                SetIfHasValue(payload, "includeContent", includeContent);
                SetIfHasValue(payload, "contentLength", contentLength);
                SetIfHasValue(payload, "contentFormat", contentFormat);
                SetIfHasValue(payload, "country", country);
                SetIfHasValue(payload, "language", language);
                SetIfHasValue(payload, "timeframe", timeframe);
                SetIfHasValue(payload, "includeAnswer", includeAnswer);
                SetIfHasValue(payload, "safeSearch", safeSearch);
                SetIfHasValue(payload, "siteSearch", siteSearch);
                SetIfHasValue(payload, "exactTerms", exactTerms);
                SetIfHasValue(payload, "excludeTerms", excludeTerms);
                SetIfHasValue(payload, "fileType", fileType);
                SetIfHasValue(payload, "includeDomains", ParseCsvToJsonArray(includeDomainsCsv));
                SetIfHasValue(payload, "excludeDomains", ParseCsvToJsonArray(excludeDomainsCsv));
                MergePayloadJson(payload, payloadJson);
            },
            cancellationToken);

    [Description("Scrape a webpage using WebSearchAPI.ai POST /scrape and return extracted structured content.")]
    [McpServerTool(Title = "WebsearchAPI Scrape", Name = "websearchapi_scrape", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> WebsearchAPI_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to scrape.")] string url,
        [Description("Output format (markdown, text, html).")]
        string returnFormat = "markdown",
        [Description("Rendering engine (direct, browser, cf-browser-rendering).")]
        string engine = "browser",
        [Description("CSS selector(s) to include.")] string? targetSelector = null,
        [Description("CSS selector(s) to remove.")] string? removeSelector = null,
        [Description("Include links summary.")] bool? withLinksSummary = null,
        [Description("Include images summary.")] bool? withImagesSummary = null,
        [Description("Generate alt text for extracted images.")] bool? withGeneratedAlt = null,
        [Description("Timeout in seconds.")] int? timeout = null,
        [Description("Execute JavaScript before scraping.")] string? injectPageScript = null,
        [Description("Use ReaderLM response shaping (e.g., readerlm-v2).")]
        string? respondWith = null,
        [Description("Send Do Not Track header.")] bool? dnt = null,
        [Description("Bypass cache and force fresh fetch.")] bool? noCache = null,
        [Description("Proxy region hint (e.g., eu, us).")]
        string? proxy = null,
        [Description("Limit extracted token budget.")] int? tokenBudget = null,
        [Description("Image retention mode (all, referenced, none).")]
        string? retainImages = null,
        [Description("Optional raw JSON object string merged into request body for advanced parameters.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await PostEndpoint(
            serviceProvider,
            requestContext,
            endpoint: "/scrape",
            payloadBuilder: payload =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);
                payload["url"] = url;
                SetIfHasValue(payload, "returnFormat", returnFormat);
                SetIfHasValue(payload, "engine", engine);
                SetIfHasValue(payload, "targetSelector", targetSelector);
                SetIfHasValue(payload, "removeSelector", removeSelector);
                SetIfHasValue(payload, "withLinksSummary", withLinksSummary);
                SetIfHasValue(payload, "withImagesSummary", withImagesSummary);
                SetIfHasValue(payload, "withGeneratedAlt", withGeneratedAlt);
                SetIfHasValue(payload, "timeout", timeout);
                SetIfHasValue(payload, "injectPageScript", injectPageScript);
                SetIfHasValue(payload, "respondWith", respondWith);
                SetIfHasValue(payload, "dnt", dnt);
                SetIfHasValue(payload, "noCache", noCache);
                SetIfHasValue(payload, "proxy", proxy);
                SetIfHasValue(payload, "tokenBudget", tokenBudget);
                SetIfHasValue(payload, "retainImages", retainImages);
                MergePayloadJson(payload, payloadJson);
            },
            cancellationToken);

    private static async Task<CallToolResult?> PostEndpoint(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        Action<JsonObject> payloadBuilder,
        CancellationToken cancellationToken)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var settings = serviceProvider.GetRequiredService<WebsearchAPISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = new JsonObject();
                payloadBuilder(payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using var client = clientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"WebsearchAPI {endpoint} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {raw}");

                return new JsonObject
                {
                    ["provider"] = "websearchapi",
                    ["baseUrl"] = BaseUrl,
                    ["endpoint"] = endpoint,
                    ["request"] = payload,
                    ["statusCode"] = (int)response.StatusCode,
                    ["response"] = TryParseJson(raw)
                };
            }));

    private static void SetIfHasValue(JsonObject payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            payload[key] = value;
    }

    private static void SetIfHasValue(JsonObject payload, string key, int? value)
    {
        if (value.HasValue)
            payload[key] = value.Value;
    }

    private static void SetIfHasValue(JsonObject payload, string key, bool? value)
    {
        if (value.HasValue)
            payload[key] = value.Value;
    }

    private static void SetIfHasValue(JsonObject payload, string key, JsonNode? value)
    {
        if (value is not null)
            payload[key] = value;
    }

    private static JsonArray? ParseCsvToJsonArray(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;

        var values = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
            return null;

        var arr = new JsonArray();
        foreach (var value in values)
            arr.Add(value);
        return arr;
    }

    private static void MergePayloadJson(JsonObject payload, string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return;

        var node = JsonNode.Parse(payloadJson)
            ?? throw new ArgumentException("payloadJson must be valid JSON object.");

        if (node is not JsonObject obj)
            throw new ArgumentException("payloadJson must be a JSON object.");

        foreach (var kv in obj)
            payload[kv.Key] = kv.Value?.DeepClone();
    }

    private static JsonNode TryParseJson(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) ?? JsonValue.Create(raw)!;
        }
        catch
        {
            return JsonValue.Create(raw)!;
        }
    }
}

