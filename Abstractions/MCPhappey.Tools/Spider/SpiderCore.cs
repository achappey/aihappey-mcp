using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Spider;

public static class SpiderCore
{
    private const string BaseUrl = "https://api.spider.cloud";

    [Description("Crawl website(s) and return extracted resources using Spider /crawl.")]
    [McpServerTool(Title = "Spider crawl", Name = "spider_crawl", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to crawl.")] string url,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/crawl", url, null, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Scrape a single page and return extracted resources using Spider /scrape.")]
    [McpServerTool(Title = "Spider scrape", Name = "spider_scrape", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to scrape.")] string url,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/scrape", url, null, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Unblock challenging websites and return data using Spider /unblocker.")]
    [McpServerTool(Title = "Spider unblocker", Name = "spider_unblocker", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Unblocker(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to unblock and fetch.")] string url,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/unblocker", url, null, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Search and optionally crawl results using Spider /search.")]
    [McpServerTool(Title = "Spider search", Name = "spider_search", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query string.")] string search,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/search", null, search, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Collect discovered links using Spider /links.")]
    [McpServerTool(Title = "Spider links", Name = "spider_links", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Links(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to collect links from.")] string url,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/links", url, null, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Take website screenshots using Spider /screenshot.")]
    [McpServerTool(Title = "Spider screenshot", Name = "spider_screenshot", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Screenshot(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to capture.")] string url,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/screenshot", url, null, null, optionsJson, returnFormat, runInBackground, cancellationToken);

    [Description("Transform HTML/text payloads using Spider /transform.")]
    [McpServerTool(Title = "Spider transform", Name = "spider_transform", Idempotent = false, OpenWorld = true, ReadOnly = false)]
    public static async Task<CallToolResult?> Spider_Transform(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("JSON string for the required 'data' field (usually array of { html, url? }).")] string dataJson,
        [Description("Optional common options as JSON object string.")] string? optionsJson = null,
        [Description("Optional response output format (application/json, application/xml, text/csv, application/jsonl).")]
        string? returnFormat = null,
        [Description("Run request in background.")] bool runInBackground = false,
        CancellationToken cancellationToken = default)
        => await PostCoreEndpoint(serviceProvider, requestContext, "/transform", null, null, dataJson, optionsJson, returnFormat, runInBackground, cancellationToken);

    private static async Task<CallToolResult?> PostCoreEndpoint(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        string? url,
        string? search,
        string? dataJson,
        string? optionsJson,
        string? returnFormat,
        bool runInBackground,
        CancellationToken cancellationToken)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var settings = serviceProvider.GetRequiredService<SpiderSettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = BuildPayload(endpoint, url, search, dataJson, optionsJson, returnFormat, runInBackground);
                var payloadText = payload.ToJsonString();

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payloadText, Encoding.UTF8, "application/json");

                using var client = clientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Spider {endpoint} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {raw}");

                return new JsonObject
                {
                    ["provider"] = "spider",
                    ["baseUrl"] = BaseUrl,
                    ["endpoint"] = endpoint,
                    ["request"] = payload,
                    ["statusCode"] = (int)response.StatusCode,
                    ["response"] = TryParseJson(raw)
                };
            }));

    private static JsonObject BuildPayload(
        string endpoint,
        string? url,
        string? search,
        string? dataJson,
        string? optionsJson,
        string? returnFormat,
        bool runInBackground)
    {
        var payload = ParseJsonObjectOrEmpty(optionsJson);

        if (NeedsUrl(endpoint))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            payload["url"] = url;
        }

        if (endpoint.Equals("/search", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(search);
            payload["search"] = search;
        }

        if (endpoint.Equals("/transform", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataJson);
            payload["data"] = ParseJsonNodeOrThrow(dataJson!, "dataJson must be valid JSON for the Spider 'data' field.");
        }

        if (!string.IsNullOrWhiteSpace(returnFormat))
            payload["return_format"] = returnFormat;

        if (runInBackground)
            payload["run_in_background"] = true;

        return payload;
    }

    private static bool NeedsUrl(string endpoint)
        => endpoint is "/crawl" or "/scrape" or "/unblocker" or "/links" or "/screenshot";

    private static JsonObject ParseJsonObjectOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var node = JsonNode.Parse(json);
        return node as JsonObject
            ?? throw new ArgumentException("optionsJson must be a JSON object.");
    }

    private static JsonNode ParseJsonNodeOrThrow(string json, string errorMessage)
        => JsonNode.Parse(json) ?? throw new ArgumentException(errorMessage);

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

