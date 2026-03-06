using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.NimbleWay;

public static class NimbleWayService
{
    private const string BaseUrl = "https://sdk.nimbleway.com";

    [Description("Execute NimbleWay Agent Run via POST /v1/agents/run and return structured extraction response.")]
    [McpServerTool(Title = "NimbleWay Agent Run", Name = "nimbleway_agent_run", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> NimbleWay_AgentRun(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Agent name to execute (for example: amazon_pdp, google_search).")]
        string agent,
        [Description("Agent params as JSON object string (required). Example: {\"asin\":\"B0DLKFK6LR\"}.")]
        string paramsJson,
        [Description("Enable agent localization when supported.")] bool? localization = null,
        [Description("Optional JSON object string merged into request body for advanced/undocumented fields.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(agent);
                var payload = new JsonObject
                {
                    ["agent"] = agent,
                    ["params"] = ParseRequiredJsonObject(paramsJson, nameof(paramsJson))
                };

                SetIfHasValue(payload, "localization", localization);
                MergePayloadJson(payload, payloadJson);

                return await PostEndpoint(serviceProvider, "/v1/agents/run", payload, cancellationToken);
            }));

    [Description("Run NimbleWay Search via POST /v1/search and return structured search results.")]
    [McpServerTool(Title = "NimbleWay Search", Name = "nimbleway_search", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> NimbleWay_Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query string.")] string query,
        [Description("Language/locale code (for example en-US).")]
        string? locale = null,
        [Description("Country code for geo-targeted results (for example US, GB).")]
        string? country = null,
        [Description("Output format: plain_text, markdown, simplified_html.")]
        string? outputFormat = null,
        [Description("Maximum number of results (1-100).")]
        int? maxResults = null,
        [Description("Search focus: general, news, location, coding, geo, shopping, social, academic.")]
        string? focus = null,
        [Description("Comma-separated content types (for example pdf,docx,documents).")]
        string? contentTypeCsv = null,
        [Description("Enable deep search mode.")] bool? deepSearch = null,
        [Description("Include LLM-generated answer summary.")] bool? includeAnswer = null,
        [Description("Comma-separated domains to exclude.")] string? excludeDomainsCsv = null,
        [Description("Comma-separated domains to include.")] string? includeDomainsCsv = null,
        [Description("Start date filter (YYYY-MM-DD or YYYY).")]
        string? startDate = null,
        [Description("End date filter (YYYY-MM-DD or YYYY).")]
        string? endDate = null,
        [Description("Time range filter: hour, day, week, month, year.")]
        string? timeRange = null,
        [Description("Maximum subagents for focus modes (1-5).")]
        int? maxSubagents = null,
        [Description("Optional JSON object string merged into request body for advanced/undocumented fields.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var payload = new JsonObject
                {
                    ["query"] = query
                };

                SetIfHasValue(payload, "locale", locale);
                SetIfHasValue(payload, "country", country);
                SetIfHasValue(payload, "output_format", outputFormat);
                SetIfHasValue(payload, "max_results", maxResults);
                SetIfHasValue(payload, "focus", focus);
                SetIfHasValue(payload, "content_type", ParseCsvToJsonArray(contentTypeCsv));
                SetIfHasValue(payload, "deep_search", deepSearch);
                SetIfHasValue(payload, "include_answer", includeAnswer);
                SetIfHasValue(payload, "exclude_domains", ParseCsvToJsonArray(excludeDomainsCsv));
                SetIfHasValue(payload, "include_domains", ParseCsvToJsonArray(includeDomainsCsv));
                SetIfHasValue(payload, "start_date", startDate);
                SetIfHasValue(payload, "end_date", endDate);
                SetIfHasValue(payload, "time_range", timeRange);
                SetIfHasValue(payload, "max_subagents", maxSubagents);
                MergePayloadJson(payload, payloadJson);

                return await PostEndpoint(serviceProvider, "/v1/search", payload, cancellationToken);
            }));

    [Description("Run NimbleWay Map via POST /v1/map and return structured mapped links.")]
    [McpServerTool(Title = "NimbleWay Map", Name = "nimbleway_map", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> NimbleWay_Map(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL to map.")] string url,
        [Description("Sitemap mode: skip, include, only.")] string? sitemap = null,
        [Description("Country code (ISO Alpha-2).")]
        string? country = null,
        [Description("Locale (for example en-US).")]
        string? locale = null,
        [Description("Domain filter: domain, subdomain, all.")]
        string? domainFilter = null,
        [Description("Maximum number of links to return (1-100000).")]
        int? limit = null,
        [Description("Optional JSON object string merged into request body for advanced/undocumented fields.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = new JsonObject
                {
                    ["url"] = url
                };

                SetIfHasValue(payload, "sitemap", sitemap);
                SetIfHasValue(payload, "country", country);
                SetIfHasValue(payload, "locale", locale);
                SetIfHasValue(payload, "domain_filter", domainFilter);
                SetIfHasValue(payload, "limit", limit);
                MergePayloadJson(payload, payloadJson);

                return await PostEndpoint(serviceProvider, "/v1/map", payload, cancellationToken);
            }));

    [Description("Run NimbleWay Extract via POST /v1/extract and return structured extraction response.")]
    [McpServerTool(Title = "NimbleWay Extract", Name = "nimbleway_extract", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> NimbleWay_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to scrape.")] string url,
        [Description("Country used to access target URL (ISO Alpha-2).")]
        string? country = null,
        [Description("State used to access target URL (US/CA ISO Alpha-2).")]
        string? state = null,
        [Description("City used to access target URL.")] string? city = null,
        [Description("Locale (for example en-US or auto).")]
        string? locale = null,
        [Description("Enable browser rendering.")] bool? render = null,
        [Description("Enable parser output.")] bool? parse = null,
        [Description("Parser object as JSON string.")] string? parserJson = null,
        [Description("Comma-separated response formats (html,markdown).")]
        string? formatsCsv = null,
        [Description("Driver: vx6, vx8, vx8-pro, vx10, vx10-pro.")]
        string? driver = null,
        [Description("Network capture array as JSON string.")] string? networkCaptureJson = null,
        [Description("Browser actions array as JSON string.")] string? browserActionsJson = null,
        [Description("Browser: chrome or firefox.")] string? browser = null,
        [Description("OS: windows, mac os, linux, android, ios.")] string? os = null,
        [Description("Disable browser rendering.")] bool? noUserbrowser = null,
        [Description("Device: desktop, mobile, tablet.")] string? device = null,
        [Description("Custom request tag.")] string? tag = null,
        [Description("Emulate XMLHttpRequest behavior.")] bool? isXhr = null,
        [Description("Use HTTP/2.")] bool? http2 = null,
        [Description("Comma-separated expected HTTP status codes.")] string? expectedStatusCodesCsv = null,
        [Description("Referrer policy: random, no-referer, same-origin.")] string? referrerType = null,
        [Description("HTTP method: GET, POST, PUT, PATCH, DELETE.")] string? method = null,
        [Description("Render options object as JSON string.")] string? renderOptionsJson = null,
        [Description("Optional JSON object string merged into request body for advanced/undocumented fields.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var payload = new JsonObject
                {
                    ["url"] = url
                };

                SetIfHasValue(payload, "country", country);
                SetIfHasValue(payload, "state", state);
                SetIfHasValue(payload, "city", city);
                SetIfHasValue(payload, "locale", locale);
                SetIfHasValue(payload, "render", render);
                SetIfHasValue(payload, "parse", parse);
                SetIfHasValue(payload, "parser", ParseOptionalJsonObject(parserJson, nameof(parserJson)));
                SetIfHasValue(payload, "formats", ParseCsvToJsonArray(formatsCsv));
                SetIfHasValue(payload, "driver", driver);
                SetIfHasValue(payload, "network_capture", ParseOptionalJsonArray(networkCaptureJson, nameof(networkCaptureJson)));
                SetIfHasValue(payload, "browser_actions", ParseOptionalJsonArray(browserActionsJson, nameof(browserActionsJson)));
                SetIfHasValue(payload, "browser", browser);
                SetIfHasValue(payload, "os", os);
                SetIfHasValue(payload, "no_userbrowser", noUserbrowser);
                SetIfHasValue(payload, "device", device);
                SetIfHasValue(payload, "tag", tag);
                SetIfHasValue(payload, "is_xhr", isXhr);
                SetIfHasValue(payload, "http2", http2);
                SetIfHasValue(payload, "expected_status_codes", ParseCsvToIntArray(expectedStatusCodesCsv));
                SetIfHasValue(payload, "referrer_type", referrerType);
                SetIfHasValue(payload, "method", method);
                SetIfHasValue(payload, "render_options", ParseOptionalJsonObject(renderOptionsJson, nameof(renderOptionsJson)));
                MergePayloadJson(payload, payloadJson);

                return await PostEndpoint(serviceProvider, "/v1/extract", payload, cancellationToken);
            }));

    [Description("Create NimbleWay crawl job, poll until terminal status, and return final crawl result as structured content.")]
    [McpServerTool(Title = "NimbleWay Create Crawl (wait)", Name = "nimbleway_crawl_create_wait", ReadOnly = true, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> NimbleWay_CrawlCreateWait(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Start URL to crawl.")] string url,
        [Description("Optional crawl task name.")] string? name = null,
        [Description("Sitemap mode: skip, include, only.")] string? sitemap = null,
        [Description("Crawl entire domain.")] bool? crawlEntireDomain = null,
        [Description("Maximum pages to crawl (1-10000).")]
        int? limit = null,
        [Description("Maximum discovery depth (1-20).")]
        int? maxDiscoveryDepth = null,
        [Description("Comma-separated include path regex patterns.")]
        string? includePathsCsv = null,
        [Description("Comma-separated exclude path regex patterns.")]
        string? excludePathsCsv = null,
        [Description("Ignore query parameters when deduplicating paths.")]
        bool? ignoreQueryParameters = null,
        [Description("Allow crawler to follow external links.")] bool? allowExternalLinks = null,
        [Description("Allow crawler to follow subdomains.")] bool? allowSubdomains = null,
        [Description("Callback object as JSON string.")] string? callbackJson = null,
        [Description("Extract options object as JSON string.")] string? extractOptionsJson = null,
        [Description("Poll interval in seconds.")] int pollIntervalSeconds = 2,
        [Description("Max wait timeout in seconds.")] int timeoutSeconds = 300,
        [Description("Optional JSON object string merged into create-crawl request body for advanced/undocumented fields.")]
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var createPayload = new JsonObject
                {
                    ["url"] = url
                };

                SetIfHasValue(createPayload, "name", name);
                SetIfHasValue(createPayload, "sitemap", sitemap);
                SetIfHasValue(createPayload, "crawl_entire_domain", crawlEntireDomain);
                SetIfHasValue(createPayload, "limit", limit);
                SetIfHasValue(createPayload, "max_discovery_depth", maxDiscoveryDepth);
                SetIfHasValue(createPayload, "include_paths", ParseCsvToJsonArray(includePathsCsv));
                SetIfHasValue(createPayload, "exclude_paths", ParseCsvToJsonArray(excludePathsCsv));
                SetIfHasValue(createPayload, "ignore_query_parameters", ignoreQueryParameters);
                SetIfHasValue(createPayload, "allow_external_links", allowExternalLinks);
                SetIfHasValue(createPayload, "allow_subdomains", allowSubdomains);
                SetIfHasValue(createPayload, "callback", ParseOptionalJsonObject(callbackJson, nameof(callbackJson)));
                SetIfHasValue(createPayload, "extract_options", ParseOptionalJsonObject(extractOptionsJson, nameof(extractOptionsJson)));
                MergePayloadJson(createPayload, payloadJson);

                var created = await PostEndpoint(serviceProvider, "/v1/crawl", createPayload, cancellationToken);
                var createResponse = created["response"] as JsonObject
                    ?? throw new Exception("NimbleWay create crawl did not return a JSON object response.");

                var crawlId = createResponse["crawl_id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(crawlId))
                    throw new Exception("NimbleWay create crawl response missing crawl_id.");

                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var settings = serviceProvider.GetRequiredService<NimbleWaySettings>();

                using var client = clientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var started = DateTimeOffset.UtcNow;
                JsonObject? lastStatusBody = null;
                int? lastStatusCode = null;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var statusResponse = await client.GetAsync($"{BaseUrl}/v1/crawl/{crawlId}", cancellationToken);
                    var statusRaw = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                    lastStatusCode = (int)statusResponse.StatusCode;

                    if (!statusResponse.IsSuccessStatusCode)
                        throw new Exception($"NimbleWay crawl status failed with {(int)statusResponse.StatusCode} {statusResponse.ReasonPhrase}: {statusRaw}");

                    var statusNode = TryParseJson(statusRaw);
                    lastStatusBody = statusNode as JsonObject
                        ?? new JsonObject { ["raw"] = statusNode };

                    var status = (lastStatusBody["status"]?.GetValue<string>() ?? string.Empty).Trim().ToLowerInvariant();
                    if (IsTerminalCrawlStatus(status))
                        break;

                    if ((DateTimeOffset.UtcNow - started).TotalSeconds >= timeoutSeconds)
                        throw new TimeoutException($"Timed out waiting for NimbleWay crawl {crawlId} after {timeoutSeconds} seconds.");

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds)), cancellationToken);
                }

                return new JsonObject
                {
                    ["provider"] = "nimbleway",
                    ["workflow"] = "create_crawl_and_wait",
                    ["baseUrl"] = BaseUrl,
                    ["create"] = created,
                    ["crawl_id"] = crawlId,
                    ["finalStatusCode"] = lastStatusCode,
                    ["final"] = lastStatusBody
                };
            }));

    private static async Task<JsonObject> PostEndpoint(
        IServiceProvider serviceProvider,
        string endpoint,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<NimbleWaySettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var client = clientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"NimbleWay {endpoint} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {raw}");

        return new JsonObject
        {
            ["provider"] = "nimbleway",
            ["baseUrl"] = BaseUrl,
            ["endpoint"] = endpoint,
            ["request"] = payload,
            ["statusCode"] = (int)response.StatusCode,
            ["response"] = TryParseJson(raw)
        };
    }

    private static bool IsTerminalCrawlStatus(string status)
        => status is "completed" or "succeeded" or "failed" or "cancelled" or "canceled";

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

    private static JsonObject ParseRequiredJsonObject(string json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException($"{paramName} is required and must be a JSON object string.");

        var parsed = JsonNode.Parse(json)
            ?? throw new ArgumentException($"{paramName} must be valid JSON object string.");

        return parsed as JsonObject
            ?? throw new ArgumentException($"{paramName} must be a JSON object.");
    }

    private static JsonObject? ParseOptionalJsonObject(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var parsed = JsonNode.Parse(json)
            ?? throw new ArgumentException($"{paramName} must be valid JSON object string.");

        return parsed as JsonObject
            ?? throw new ArgumentException($"{paramName} must be a JSON object.");
    }

    private static JsonArray? ParseOptionalJsonArray(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var parsed = JsonNode.Parse(json)
            ?? throw new ArgumentException($"{paramName} must be valid JSON array string.");

        return parsed as JsonArray
            ?? throw new ArgumentException($"{paramName} must be a JSON array.");
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

    private static JsonArray? ParseCsvToIntArray(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;

        var arr = new JsonArray();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var parsed))
                arr.Add(parsed);
            else
                throw new ArgumentException($"Invalid integer in CSV list: '{part}'.");
        }

        return arr.Count > 0 ? arr : null;
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

