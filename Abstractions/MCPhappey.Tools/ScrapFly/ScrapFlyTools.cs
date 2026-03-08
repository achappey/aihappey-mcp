using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ScrapFly;

public static class ScrapFlyTools
{
    private const string BaseUrl = "https://api.scrapfly.io";
    private const string JsonMimeType = "application/json";

    [Description("Scrape a webpage with ScrapFly Web Scraping API. Supports ScrapFly formats, anti-bot options, and optional SharePoint/OneDrive file outputs via uploaded artifacts.")]
    [McpServerTool(Title = "ScrapFly scrape", Name = "scrapfly_scrape", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to scrape.")] string url,
        [Description("Output format: raw, clean_html, json, markdown, or text.")] string format = "raw",
        [Description("Optional query-string style headers, one per line, e.g. Header=Value.")] string? headers = null,
        [Description("Optional request body for POST/PUT/PATCH scraping.")] string? requestBody = null,
        [Description("HTTP method: GET, POST, PUT, PATCH, or HEAD.")] string method = "GET",
        [Description("Proxy pool, e.g. public_datacenter_pool or public_residential_pool.")] string proxyPool = "public_datacenter_pool",
        [Description("Country code or weighted country expression.")] string? country = null,
        [Description("Language header hint.")] string? lang = null,
        [Description("Enable ASP anti-scraping protection.")] bool asp = false,
        [Description("Enable JS rendering.")] bool renderJs = false,
        [Description("Selector or XPath to wait for.")] string? waitForSelector = null,
        [Description("Rendering wait in milliseconds.")] int? renderingWait = null,
        [Description("Enable response caching.")] bool cache = false,
        [Description("Cache TTL in seconds.")] int? cacheTtl = null,
        [Description("Force cache clear for this request.")] bool cacheClear = false,
        [Description("Return proxified response body directly when possible.")] bool proxifiedResponse = false,
        [Description("Enable debug mode and dashboard logging.")] bool debug = false,
        [Description("Optional extraction prompt for ScrapFly extraction during scrape.")] string? extractionPrompt = null,
        [Description("Optional extraction model, e.g. product or article.")] string? extractionModel = null,
        [Description("Optional extraction template string.")] string? extractionTemplate = null,
        [Description("Optional output filename base when uploaded artifacts are created.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var payload = new JsonObject();
                if (!string.IsNullOrWhiteSpace(requestBody))
                    payload["requestBody"] = requestBody;

                var query = new Dictionary<string, string?>
                {
                    ["url"] = url,
                    ["format"] = NormalizeScrapeFormat(format),
                    ["proxy_pool"] = proxyPool,
                    ["country"] = country,
                    ["lang"] = lang,
                    ["headers"] = headers,
                    ["asp"] = ToLowerBoolean(asp),
                    ["render_js"] = ToLowerBoolean(renderJs),
                    ["wait_for_selector"] = waitForSelector,
                    ["rendering_wait"] = renderingWait?.ToString(),
                    ["cache"] = ToLowerBoolean(cache),
                    ["cache_ttl"] = cacheTtl?.ToString(),
                    ["cache_clear"] = ToLowerBoolean(cacheClear),
                    ["proxified_response"] = ToLowerBoolean(proxifiedResponse),
                    ["debug"] = ToLowerBoolean(debug),
                    ["extraction_prompt"] = extractionPrompt,
                    ["extraction_model"] = extractionModel,
                    ["extraction_template"] = extractionTemplate,
                };

                return await SendAsync(
                    serviceProvider,
                    requestContext,
                    HttpMethod.Parse(method.ToUpperInvariant()),
                    BuildUrl("/scrape", query),
                    payload.Count > 0 ? payload.ToJsonString() : null,
                    isBinaryResponse: proxifiedResponse || IsBinaryLikeScrapeFormat(format),
                    filenameBase: filename,
                    defaultExtension: InferScrapeExtension(format),
                    cancellationToken: cancellationToken);
            }));

    [Description("Capture a website screenshot with ScrapFly Screenshot API and upload the image output as a resource link block.")]
    [McpServerTool(Title = "ScrapFly screenshot", Name = "scrapfly_screenshot", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Screenshot(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to capture.")] string url,
        [Description("Image format: jpg, png, webp, or gif.")] string format = "jpg",
        [Description("Capture mode: viewport, fullpage, CSS selector, or XPath.")] string capture = "viewport",
        [Description("Viewport resolution, e.g. 1920x1080.")] string resolution = "1920x1080",
        [Description("Country code for proxy location.")] string? country = null,
        [Description("Timeout in milliseconds.")] int? timeout = null,
        [Description("Rendering wait in milliseconds.")] int? renderingWait = null,
        [Description("Selector or XPath to wait for before capture.")] string? waitForSelector = null,
        [Description("Options as comma-separated values, e.g. block_banners,dark_mode.")] string? options = null,
        [Description("Enable auto-scroll.")] bool autoScroll = false,
        [Description("Base64-encoded JavaScript to execute before capture.")] string? js = null,
        [Description("Enable caching.")] bool cache = false,
        [Description("Cache TTL in seconds.")] int? cacheTtl = null,
        [Description("Force cache clear for this request.")] bool cacheClear = false,
        [Description("Vision deficiency simulation mode.")] string? visionDeficiency = null,
        [Description("Output filename base.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var query = new Dictionary<string, string?>
                {
                    ["url"] = url,
                    ["format"] = NormalizeScreenshotFormat(format),
                    ["capture"] = capture,
                    ["resolution"] = resolution,
                    ["country"] = country,
                    ["timeout"] = timeout?.ToString(),
                    ["rendering_wait"] = renderingWait?.ToString(),
                    ["wait_for_selector"] = waitForSelector,
                    ["options"] = options,
                    ["auto_scroll"] = ToLowerBoolean(autoScroll),
                    ["js"] = js,
                    ["cache"] = ToLowerBoolean(cache),
                    ["cache_ttl"] = cacheTtl?.ToString(),
                    ["cache_clear"] = ToLowerBoolean(cacheClear),
                    ["vision_deficiency"] = visionDeficiency,
                };

                return await SendAsync(
                    serviceProvider,
                    requestContext,
                    HttpMethod.Get,
                    BuildUrl("/screenshot", query),
                    body: null,
                    isBinaryResponse: true,
                    filenameBase: filename,
                    defaultExtension: NormalizeScreenshotFormat(format),
                    cancellationToken: cancellationToken);
            }));

    [Description("Extract structured data or answers from text, markdown, HTML, or other readable fileUrl content with ScrapFly Extraction API.")]
    [McpServerTool(Title = "ScrapFly extraction", Name = "scrapfly_extraction", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Extraction(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL or public URL to extract content from. Supports SharePoint/OneDrive.")] string fileUrl,
        [Description("Content type sent to ScrapFly, e.g. text/html, text/markdown, text/plain, or text/xml.")] string contentType = "text/plain",
        [Description("Optional extraction prompt.")] string? extractionPrompt = null,
        [Description("Optional extraction model, e.g. product, article, or review_list.")] string? extractionModel = null,
        [Description("Optional extraction template string.")] string? extractionTemplate = null,
        [Description("Optional base URL for resolving relative links.")] string? url = null,
        [Description("Document charset. Default: auto.")] string charset = "auto",
        [Description("Extraction timeout in seconds.")] int? timeout = null,
        [Description("Optional webhook name.")] string? webhookName = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var input = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download extraction input from fileUrl.");

                var query = new Dictionary<string, string?>
                {
                    ["content_type"] = contentType,
                    ["extraction_prompt"] = extractionPrompt,
                    ["extraction_model"] = extractionModel,
                    ["extraction_template"] = extractionTemplate,
                    ["url"] = url,
                    ["charset"] = charset,
                    ["timeout"] = timeout?.ToString(),
                    ["webhook_name"] = webhookName,
                };

                return await SendAsync(
                    serviceProvider,
                    requestContext,
                    HttpMethod.Post,
                    BuildUrl("/extraction", query),
                    body: input.Contents.ToString(),
                    isBinaryResponse: false,
                    filenameBase: null,
                    defaultExtension: "json",
                    explicitContentType: contentType,
                    cancellationToken: cancellationToken);
            }));

    [Description("Start a ScrapFly crawler job, poll until it finishes, and optionally return URLs, contents, or uploaded artifacts when ready.")]
    [McpServerTool(Title = "ScrapFly crawl", Name = "scrapfly_crawl", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Seed URL to crawl.")] string url,
        [Description("Maximum number of pages to crawl. Use 0 for unlimited.")] int pageLimit = 100,
        [Description("Maximum crawl depth.")] int maxDepth = 2,
        [Description("Exclude path patterns separated by newlines or commas.")] string? excludePaths = null,
        [Description("Include-only path patterns separated by newlines or commas.")] string? includeOnlyPaths = null,
        [Description("Ignore base path restriction.")] bool ignoreBasePathRestriction = false,
        [Description("Follow external links.")] bool followExternalLinks = false,
        [Description("Allowed external domains separated by newlines or commas.")] string? allowedExternalDomains = null,
        [Description("Follow internal subdomains.")] bool followInternalSubdomains = true,
        [Description("Allowed internal subdomains separated by newlines or commas.")] string? allowedInternalSubdomains = null,
        [Description("Browser rendering delay in milliseconds. 0 disables browser rendering.")] int renderingDelay = 0,
        [Description("Maximum concurrency. 0 uses account default.")] int maxConcurrency = 0,
        [Description("Headers as JSON object string.")] string? headersJson = null,
        [Description("Delay between requests in milliseconds.")] string? delay = null,
        [Description("Custom user agent.")] string? userAgent = null,
        [Description("Use sitemap.xml discovery.")] bool useSitemaps = false,
        [Description("Respect robots.txt.")] bool respectRobotsTxt = true,
        [Description("Enable cache.")] bool cache = false,
        [Description("Cache TTL in seconds.")] int? cacheTtl = null,
        [Description("Force cache clear.")] bool cacheClear = false,
        [Description("Ignore rel=nofollow links.")] bool ignoreNoFollow = false,
        [Description("Content formats separated by commas, e.g. html,markdown,text.")] string? contentFormats = null,
        [Description("Maximum crawl duration in seconds.")] int? maxDuration = null,
        [Description("Maximum API credits to spend.")] int? maxApiCredit = null,
        [Description("Extraction rules JSON string.")] string? extractionRulesJson = null,
        [Description("Webhook name configured in ScrapFly.")] string? webhookName = null,
        [Description("Webhook events separated by commas.")] string? webhookEvents = null,
        [Description("Proxy pool.")] string proxyPool = "public_datacenter_pool",
        [Description("Country code or weighted country expression.")] string? country = null,
        [Description("Enable anti-scraping protection.")] bool asp = false,
        [Description("Polling interval in seconds while waiting for the crawl to finish.")] int pollIntervalSeconds = 2,
        [Description("Final result to return after the crawl is finished: status, urls, contents, or artifact.")] string resultType = "status",
        [Description("Result format when resultType=contents.")] string resultFormat = "markdown",
        [Description("Optional specific URL when resultType=contents.")] string? resultUrl = null,
        [Description("Artifact type when resultType=artifact: warc or har.")] string artifactType = "warc",
        [Description("Optional output filename base for uploaded result files.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new ScrapFlyCrawlRequest
                    {
                        Url = url,
                        PageLimit = pageLimit,
                        MaxDepth = maxDepth,
                        ExcludePaths = excludePaths,
                        IncludeOnlyPaths = includeOnlyPaths,
                        IgnoreBasePathRestriction = ignoreBasePathRestriction,
                        FollowExternalLinks = followExternalLinks,
                        AllowedExternalDomains = allowedExternalDomains,
                        FollowInternalSubdomains = followInternalSubdomains,
                        AllowedInternalSubdomains = allowedInternalSubdomains,
                        RenderingDelay = renderingDelay,
                        MaxConcurrency = maxConcurrency,
                        HeadersJson = headersJson,
                        Delay = delay,
                        UserAgent = userAgent,
                        UseSitemaps = useSitemaps,
                        RespectRobotsTxt = respectRobotsTxt,
                        Cache = cache,
                        CacheTtl = cacheTtl,
                        CacheClear = cacheClear,
                        IgnoreNoFollow = ignoreNoFollow,
                        ContentFormats = contentFormats,
                        MaxDuration = maxDuration,
                        MaxApiCredit = maxApiCredit,
                        ExtractionRulesJson = extractionRulesJson,
                        WebhookName = webhookName,
                        WebhookEvents = webhookEvents,
                        ProxyPool = proxyPool,
                        Country = country,
                        Asp = asp,
                        PollIntervalSeconds = Math.Max(1, pollIntervalSeconds),
                        ResultType = NormalizeResultType(resultType),
                        ResultFormat = NormalizeScrapeFormat(resultFormat),
                        ResultUrl = resultUrl,
                        ArtifactType = NormalizeArtifactType(artifactType),
                        Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "No input data provided".ToErrorCallToolResponse();

                var payload = new JsonObject
                {
                    ["url"] = typed.Url,
                    ["page_limit"] = typed.PageLimit,
                    ["max_depth"] = typed.MaxDepth,
                    ["ignore_base_path_restriction"] = typed.IgnoreBasePathRestriction,
                    ["follow_external_links"] = typed.FollowExternalLinks,
                    ["follow_internal_subdomains"] = typed.FollowInternalSubdomains,
                    ["rendering_delay"] = typed.RenderingDelay,
                    ["max_concurrency"] = typed.MaxConcurrency,
                    ["delay"] = typed.Delay,
                    ["user_agent"] = typed.UserAgent,
                    ["use_sitemaps"] = typed.UseSitemaps,
                    ["respect_robots_txt"] = typed.RespectRobotsTxt,
                    ["cache"] = typed.Cache,
                    ["cache_clear"] = typed.CacheClear,
                    ["ignore_no_follow"] = typed.IgnoreNoFollow,
                    ["max_duration"] = typed.MaxDuration,
                    ["max_api_credit"] = typed.MaxApiCredit,
                    ["webhook_name"] = typed.WebhookName,
                    ["proxy_pool"] = typed.ProxyPool,
                    ["country"] = typed.Country,
                    ["asp"] = typed.Asp,
                };

                AddArrayIfAny(payload, "exclude_paths", typed.ExcludePaths);
                AddArrayIfAny(payload, "include_only_paths", typed.IncludeOnlyPaths);
                AddArrayIfAny(payload, "allowed_external_domains", typed.AllowedExternalDomains);
                AddArrayIfAny(payload, "allowed_internal_subdomains", typed.AllowedInternalSubdomains);
                AddArrayIfAny(payload, "content_formats", typed.ContentFormats);
                AddArrayIfAny(payload, "webhook_events", typed.WebhookEvents);
                AddJsonIfAny(payload, "headers", typed.HeadersJson);
                AddJsonIfAny(payload, "extraction_rules", typed.ExtractionRulesJson);
                if (typed.CacheTtl.HasValue) payload["cache_ttl"] = typed.CacheTtl.Value;

                var created = await SendJsonAsync(
                    serviceProvider,
                    requestContext,
                    HttpMethod.Post,
                    BuildUrl("/crawl", null),
                    payload.ToJsonString(),
                    explicitContentType: JsonMimeType,
                    cancellationToken: cancellationToken);

                var crawlerUuid = created?["raw"]?["uuid"]?.GetValue<string>()
                    ?? created?["uuid"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("ScrapFly crawl did not return a crawler uuid.");

                JsonNode? latestStatus = null;
                while (true)
                {
                    latestStatus = await SendJsonAsync(
                        serviceProvider,
                        requestContext,
                        HttpMethod.Get,
                        BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/status", null),
                        null,
                        JsonMimeType,
                        cancellationToken);

                    var isFinished = latestStatus?["raw"]?["is_finished"]?.GetValue<bool>()
                        ?? latestStatus?["is_finished"]?.GetValue<bool>()
                        ?? false;

                    if (isFinished)
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, typed.PollIntervalSeconds)), cancellationToken);
                }

                var isSuccess = latestStatus?["raw"]?["is_success"]?.GetValue<bool?>()
                    ?? latestStatus?["is_success"]?.GetValue<bool?>();

                if (isSuccess == false)
                    return new CallToolResult
                    {
                        StructuredContent = latestStatus,
                        Content = [ (latestStatus?.ToJsonString() ?? "ScrapFly crawl failed.").ToTextContentBlock() ]
                    };

                return typed.ResultType switch
                {
                    "urls" => await SendAsync(
                        serviceProvider,
                        requestContext,
                        HttpMethod.Get,
                        BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/urls", new Dictionary<string, string?>()),
                        null,
                        false,
                        null,
                        "json",
                        cancellationToken),
                    "contents" => await SendAsync(
                        serviceProvider,
                        requestContext,
                        HttpMethod.Get,
                        BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/contents", new Dictionary<string, string?>
                        {
                            ["format"] = typed.ResultFormat,
                            ["url"] = typed.ResultUrl
                        }),
                        null,
                        false,
                        typed.Filename,
                        InferScrapeExtension(typed.ResultFormat),
                        cancellationToken),
                    "artifact" => await SendAsync(
                        serviceProvider,
                        requestContext,
                        HttpMethod.Get,
                        BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/artifact", new Dictionary<string, string?>
                        {
                            ["type"] = typed.ArtifactType
                        }),
                        null,
                        true,
                        typed.Filename,
                        typed.ArtifactType.Equals("har", StringComparison.OrdinalIgnoreCase) ? "har" : "warc.gz",
                        cancellationToken,
                        resourceOnlyOnBinary: true),
                    _ => new CallToolResult
                    {
                        StructuredContent = latestStatus,
                        Content = [ (latestStatus?.ToJsonString() ?? "{}").ToTextContentBlock() ]
                    }
                };
            }));

    [Description("List crawled URLs for a ScrapFly crawler job.")]
    [McpServerTool(Title = "ScrapFly crawl URLs", Name = "scrapfly_crawl_urls", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Crawl_Urls(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Crawler UUID.")] string crawlerUuid,
        [Description("Filter status: visited, pending, or failed.")] string? status = null,
        [Description("Page number.")] int page = 1,
        [Description("Items per page.")] int perPage = 100,
        CancellationToken cancellationToken = default)
        => await SendAsync(
            serviceProvider,
            requestContext,
            HttpMethod.Get,
            BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/urls", new Dictionary<string, string?>
            {
                ["status"] = status,
                ["page"] = page.ToString(),
                ["per_page"] = perPage.ToString()
            }),
            null,
            false,
            null,
            "json",
            cancellationToken);

    [Description("Retrieve crawl contents for one URL or all pages in a selected format.")]
    [McpServerTool(Title = "ScrapFly crawl contents", Name = "scrapfly_crawl_contents", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Crawl_Contents(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Crawler UUID.")] string crawlerUuid,
        [Description("Content format to retrieve.")] string format = "markdown",
        [Description("Optional specific page URL.")] string? url = null,
        [Description("Optional output filename base when content is uploaded as artifact.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await SendAsync(
            serviceProvider,
            requestContext,
            HttpMethod.Get,
            BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/contents", new Dictionary<string, string?>
            {
                ["format"] = format,
                ["url"] = url
            }),
            null,
            isBinaryResponse: false,
            filenameBase: filename,
            defaultExtension: InferScrapeExtension(format),
            cancellationToken: cancellationToken);

    [Description("Download a crawl artifact such as WARC or HAR, upload it to SharePoint/OneDrive, and return only a resource link block in content.")]
    [McpServerTool(Title = "ScrapFly crawl artifact", Name = "scrapfly_crawl_artifact", Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    public static async Task<CallToolResult?> ScrapFly_Crawl_Artifact(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Crawler UUID.")] string crawlerUuid,
        [Description("Artifact type: warc or har.")] string type = "warc",
        [Description("Optional output filename base.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await SendAsync(
            serviceProvider,
            requestContext,
            HttpMethod.Get,
            BuildUrl($"/crawl/{Uri.EscapeDataString(crawlerUuid)}/artifact", new Dictionary<string, string?>
            {
                ["type"] = type
            }),
            null,
            isBinaryResponse: true,
            filenameBase: filename,
            defaultExtension: type.Equals("har", StringComparison.OrdinalIgnoreCase) ? "har" : "warc.gz",
            cancellationToken: cancellationToken,
            resourceOnlyOnBinary: true);

    private static async Task<CallToolResult?> SendAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        HttpMethod method,
        string url,
        string? body,
        bool isBinaryResponse,
        string? filenameBase,
        string defaultExtension,
        CancellationToken cancellationToken,
        string? explicitContentType = null,
        bool resourceOnlyOnBinary = false)
    {
        using var response = await SendRequestAsync(serviceProvider, method, url, body, isBinaryResponse ? "*/*" : JsonMimeType, explicitContentType, cancellationToken);

        if (isBinaryResponse)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var safeName = (filenameBase?.ToOutputFileName() ?? requestContext.ToOutputFileName()).TrimEnd('.');
            var uploadName = defaultExtension.Contains('.')
                ? $"{safeName}.{defaultExtension}"
                : $"{safeName}.{defaultExtension}";

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            if (uploaded == null)
                throw new InvalidOperationException("ScrapFly binary output upload failed.");

            return resourceOnlyOnBinary
                ? uploaded.ToResourceLinkCallToolResponse()
                : new CallToolResult
                {
                    StructuredContent = new JsonObject
                    {
                        ["provider"] = "scrapfly",
                        ["url"] = url,
                        ["statusCode"] = (int)response.StatusCode,
                        ["headers"] = ResponseHeadersToJson(response.Headers, response.Content.Headers),
                        ["output"] = new JsonObject
                        {
                            ["type"] = "resource",
                            ["filename"] = uploadName,
                            ["extension"] = defaultExtension,
                            ["contentType"] = response.Content.Headers.ContentType?.MediaType
                        }
                    },
                    Content = [ uploaded ]
                };
        }

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var structured = new JsonObject
        {
            ["provider"] = "scrapfly",
            ["url"] = url,
            ["statusCode"] = (int)response.StatusCode,
            ["headers"] = ResponseHeadersToJson(response.Headers, response.Content.Headers),
            ["raw"] = TryParseJson(raw)
        };

        return new CallToolResult
        {
            StructuredContent = structured,
            Content = [ raw.ToTextContentBlock() ]
        };
    }

    private static async Task<JsonNode> SendJsonAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        HttpMethod method,
        string url,
        string? body,
        string? explicitContentType,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(serviceProvider, method, url, body, JsonMimeType, explicitContentType, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return new JsonObject
        {
            ["provider"] = "scrapfly",
            ["url"] = url,
            ["statusCode"] = (int)response.StatusCode,
            ["headers"] = ResponseHeadersToJson(response.Headers, response.Content.Headers),
            ["raw"] = TryParseJson(raw)
        };
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string url,
        string? body,
        string accept,
        string? explicitContentType,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<ScrapFlySettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var client = clientFactory.CreateClient();
        var request = new HttpRequestMessage(method, AppendApiKey(url, settings.ApiKey));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, explicitContentType ?? JsonMimeType);

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            request.Dispose();
            client.Dispose();
            response.Dispose();
            throw new InvalidOperationException($"ScrapFly request failed ({(int)response.StatusCode}): {errorBody}");
        }

        request.Dispose();
        client.Dispose();
        return response;
    }

    private static string BuildUrl(string path, IDictionary<string, string?>? query)
    {
        var builder = new StringBuilder($"{BaseUrl}{path}");
        if (query == null || query.Count == 0)
            return builder.ToString();

        var parts = query
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}")
            .ToList();

        if (parts.Count > 0)
            builder.Append('?').Append(string.Join("&", parts));

        return builder.ToString();
    }

    private static string AppendApiKey(string url, string apiKey)
        => url.Contains('?')
            ? $"{url}&key={Uri.EscapeDataString(apiKey)}"
            : $"{url}?key={Uri.EscapeDataString(apiKey)}";

    private static JsonObject ResponseHeadersToJson(HttpResponseHeaders headers, HttpContentHeaders contentHeaders)
    {
        var json = new JsonObject();
        foreach (var header in headers)
            json[header.Key] = string.Join(", ", header.Value);
        foreach (var header in contentHeaders)
            json[header.Key] = string.Join(", ", header.Value);
        return json;
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

    private static void AddArrayIfAny(JsonObject payload, string propertyName, string? values)
    {
        var parsed = SplitList(values);
        if (parsed.Count == 0)
            return;

        var array = new JsonArray();
        foreach (var item in parsed)
            array.Add(item);
        payload[propertyName] = array;
    }

    private static void AddJsonIfAny(JsonObject payload, string propertyName, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        payload[propertyName] = JsonNode.Parse(json) ?? throw new ArgumentException($"{propertyName} must be valid JSON.");
    }

    private static List<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([',', '\n', ';', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string NormalizeScrapeFormat(string format)
        => string.IsNullOrWhiteSpace(format) ? "raw" : format.Trim().ToLowerInvariant();

    private static string NormalizeScreenshotFormat(string format)
        => string.IsNullOrWhiteSpace(format) ? "jpg" : format.Trim().ToLowerInvariant();

    private static string NormalizeResultType(string resultType)
        => string.IsNullOrWhiteSpace(resultType) ? "status" : resultType.Trim().ToLowerInvariant() switch
        {
            "status" or "urls" or "contents" or "artifact" => resultType.Trim().ToLowerInvariant(),
            _ => "status"
        };

    private static string NormalizeArtifactType(string artifactType)
        => string.Equals(artifactType?.Trim(), "har", StringComparison.OrdinalIgnoreCase) ? "har" : "warc";

    private static string ToLowerBoolean(bool value) => value ? "true" : "false";

    private static bool IsBinaryLikeScrapeFormat(string format)
        => string.Equals(format?.Trim(), "raw", StringComparison.OrdinalIgnoreCase);

    private static string InferScrapeExtension(string format)
        => NormalizeScrapeFormat(format) switch
        {
            "markdown" => "md",
            "text" => "txt",
            "clean_html" => "html",
            "json" => "json",
            _ => "html"
        };
}

public sealed class ScrapFlyCrawlRequest
{
    [Required]
    [JsonPropertyName("url")]
    [Description("Seed URL to crawl.")]
    public string Url { get; set; } = default!;

    [JsonPropertyName("pageLimit")]
    public int PageLimit { get; set; } = 100;

    [JsonPropertyName("maxDepth")]
    public int MaxDepth { get; set; } = 2;

    [JsonPropertyName("excludePaths")]
    public string? ExcludePaths { get; set; }

    [JsonPropertyName("includeOnlyPaths")]
    public string? IncludeOnlyPaths { get; set; }

    [JsonPropertyName("ignoreBasePathRestriction")]
    public bool IgnoreBasePathRestriction { get; set; }

    [JsonPropertyName("followExternalLinks")]
    public bool FollowExternalLinks { get; set; }

    [JsonPropertyName("allowedExternalDomains")]
    public string? AllowedExternalDomains { get; set; }

    [JsonPropertyName("followInternalSubdomains")]
    public bool FollowInternalSubdomains { get; set; } = true;

    [JsonPropertyName("allowedInternalSubdomains")]
    public string? AllowedInternalSubdomains { get; set; }

    [JsonPropertyName("renderingDelay")]
    public int RenderingDelay { get; set; }

    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; set; }

    [JsonPropertyName("headersJson")]
    public string? HeadersJson { get; set; }

    [JsonPropertyName("delay")]
    public string? Delay { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("useSitemaps")]
    public bool UseSitemaps { get; set; }

    [JsonPropertyName("respectRobotsTxt")]
    public bool RespectRobotsTxt { get; set; } = true;

    [JsonPropertyName("cache")]
    public bool Cache { get; set; }

    [JsonPropertyName("cacheTtl")]
    public int? CacheTtl { get; set; }

    [JsonPropertyName("cacheClear")]
    public bool CacheClear { get; set; }

    [JsonPropertyName("ignoreNoFollow")]
    public bool IgnoreNoFollow { get; set; }

    [JsonPropertyName("contentFormats")]
    public string? ContentFormats { get; set; }

    [JsonPropertyName("maxDuration")]
    public int? MaxDuration { get; set; }

    [JsonPropertyName("maxApiCredit")]
    public int? MaxApiCredit { get; set; }

    [JsonPropertyName("extractionRulesJson")]
    public string? ExtractionRulesJson { get; set; }

    [JsonPropertyName("webhookName")]
    public string? WebhookName { get; set; }

    [JsonPropertyName("webhookEvents")]
    public string? WebhookEvents { get; set; }

    [JsonPropertyName("proxyPool")]
    public string ProxyPool { get; set; } = "public_datacenter_pool";

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("asp")]
    public bool Asp { get; set; }

    [Range(1, 300)]
    [JsonPropertyName("pollIntervalSeconds")]
    public int PollIntervalSeconds { get; set; } = 2;

    [JsonPropertyName("resultType")]
    public string ResultType { get; set; } = "status";

    [JsonPropertyName("resultFormat")]
    public string ResultFormat { get; set; } = "markdown";

    [JsonPropertyName("resultUrl")]
    public string? ResultUrl { get; set; }

    [JsonPropertyName("artifactType")]
    public string ArtifactType { get; set; } = "warc";

    [Required]
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = default!;
}
