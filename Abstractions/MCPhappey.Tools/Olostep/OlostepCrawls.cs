using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Olostep;

public static class OlostepCrawls
{
    [Description("Start an Olostep crawl, optionally wait for completion by polling crawl status, and return structured crawl metadata.")]
    [McpServerTool(Title = "Olostep create crawl", Name = "olostep_crawl_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Crawl_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Starting URL for the crawl.")] string start_url,
        [Description("Maximum number of pages to crawl.")] int max_pages,
        [Description("Optional include path patterns as newline-separated or comma-separated glob strings.")] string? include_urls = null,
        [Description("Optional exclude path patterns as newline-separated or comma-separated glob strings.")] string? exclude_urls = null,
        [Description("Optional maximum crawl depth.")] int? max_depth = null,
        [Description("Include first-degree external links.")] bool include_external = false,
        [Description("Include subdomains of the target website.")] bool include_subdomain = false,
        [Description("Optional search query used to prioritize relevant links.")] string? search_query = null,
        [Description("Optional number of top relevant links to crawl per page when using search_query.")] int? top_n = null,
        [Description("Optional webhook URL to receive crawl completion notifications.")] string? webhook = null,
        [Description("Optional crawl timeout in seconds.")] int? timeout = null,
        [Description("Respect robots.txt rules during crawling.")] bool follow_robots_txt = true,
        [Description("Optional scrape output formats as newline-separated or comma-separated strings such as html, markdown, text, json, screenshot.")] string? scrape_formats = null,
        [Description("Optional Olostep parser name applied to each crawled page, for example @olostep/extract-emails.")] string? scrape_parser = null,
        [Description("Wait for crawl completion by polling crawl status before returning.")] bool wait_for_completion = false,
        [Description("Polling interval in seconds when wait_for_completion is true.")] int poll_interval_seconds = 5,
        [Description("Maximum seconds to wait when wait_for_completion is true.")] int max_wait_seconds = 600,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepCreateCrawlRequest
                {
                    StartUrl = start_url,
                    MaxPages = max_pages,
                    IncludeUrls = include_urls,
                    ExcludeUrls = exclude_urls,
                    MaxDepth = max_depth,
                    IncludeExternal = include_external,
                    IncludeSubdomain = include_subdomain,
                    SearchQuery = search_query,
                    TopN = top_n,
                    Webhook = webhook,
                    Timeout = timeout,
                    FollowRobotsTxt = follow_robots_txt,
                    ScrapeFormats = scrape_formats,
                    ScrapeParser = scrape_parser,
                    WaitForCompletion = wait_for_completion,
                    PollIntervalSeconds = poll_interval_seconds,
                    MaxWaitSeconds = max_wait_seconds
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.StartUrl);

            var payload = new JsonObject
            {
                ["start_url"] = typed.StartUrl,
                ["max_pages"] = typed.MaxPages,
                ["include_external"] = typed.IncludeExternal,
                ["include_subdomain"] = typed.IncludeSubdomain,
                ["follow_robots_txt"] = typed.FollowRobotsTxt
            };

            OlostepHelpers.AddIfNotNull(payload, "include_urls", OlostepHelpers.ParseDelimitedList(typed.IncludeUrls));
            OlostepHelpers.AddIfNotNull(payload, "exclude_urls", OlostepHelpers.ParseDelimitedList(typed.ExcludeUrls));
            OlostepHelpers.AddIfNotNull(payload, "max_depth", typed.MaxDepth);
            OlostepHelpers.AddIfNotNull(payload, "search_query", typed.SearchQuery);
            OlostepHelpers.AddIfNotNull(payload, "top_n", typed.TopN);
            OlostepHelpers.AddIfNotNull(payload, "webhook", typed.Webhook);
            OlostepHelpers.AddIfNotNull(payload, "timeout", typed.Timeout);

            var scrapeOptions = new JsonObject();
            OlostepHelpers.AddIfNotNull(scrapeOptions, "formats", OlostepHelpers.ParseDelimitedList(typed.ScrapeFormats));
            OlostepHelpers.AddIfNotNull(scrapeOptions, "parser", typed.ScrapeParser);
            if (scrapeOptions.Count > 0)
                payload["scrape_options"] = scrapeOptions;

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.PostJsonAsync("v1/crawls", payload, cancellationToken) ?? new JsonObject();

            var crawlId = OlostepHelpers.GetString(response, "id");
            var finalResponse = response;
            var status = OlostepHelpers.GetString(finalResponse, "status") ?? "unknown";
            var pollCount = 0;
            var timedOut = false;

            if (typed.WaitForCompletion && !string.IsNullOrWhiteSpace(crawlId) && !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                int? progressCounter = 0;
                var startedAt = DateTimeOffset.UtcNow;

                while (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromSeconds(typed.MaxWaitSeconds))
                    {
                        timedOut = true;
                        break;
                    }

                    progressCounter = await requestContext.Server.SendProgressNotificationAsync(
                        requestContext,
                        progressCounter,
                        $"Olostep crawl {crawlId} status={status}. Waiting {typed.PollIntervalSeconds}s before the next poll.",
                        cancellationToken: cancellationToken);

                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, typed.PollIntervalSeconds)), cancellationToken);
                    finalResponse = await client.GetJsonAsync($"v1/crawls/{Uri.EscapeDataString(crawlId)}", null, cancellationToken) ?? new JsonObject();
                    status = OlostepHelpers.GetString(finalResponse, "status") ?? status;
                    pollCount++;
                }
            }

            var pagesCount = finalResponse["pages_count"]?.GetValue<int?>();
            var summary = typed.WaitForCompletion
                ? timedOut
                    ? $"Olostep crawl started. Id={crawlId ?? "unknown"}. Polling timed out with status={status}."
                    : $"Olostep crawl finished polling. Id={crawlId ?? "unknown"}. Status={status}."
                : $"Olostep crawl started. Id={crawlId ?? "unknown"}. Status={status}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/crawls",
                    payload,
                    finalResponse,
                    ("id", crawlId),
                    ("status", status),
                    ("pagesCount", pagesCount),
                    ("waitForCompletion", typed.WaitForCompletion),
                    ("pollCount", pollCount),
                    ("timedOut", timedOut)).ToJsonElement(),
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Retrieve an Olostep crawl object by crawl id.")]
    [McpServerTool(Title = "Olostep get crawl", Name = "olostep_crawl_get", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Crawl_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Crawl identifier returned by Olostep crawl creation.")] string crawl_id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepGetCrawlRequest
                {
                    CrawlId = crawl_id
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.CrawlId);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.GetJsonAsync($"v1/crawls/{Uri.EscapeDataString(typed.CrawlId)}", null, cancellationToken) ?? new JsonObject();
            var status = OlostepHelpers.GetString(response, "status");
            var pagesCount = response["pages_count"]?.GetValue<int?>();
            var summary = $"Olostep crawl retrieved. Id={typed.CrawlId}. Status={status ?? "unknown"}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/crawls/{crawl_id}",
                    new { crawl_id = typed.CrawlId },
                    response,
                    ("id", OlostepHelpers.GetString(response, "id") ?? typed.CrawlId),
                    ("status", status),
                    ("pagesCount", pagesCount)).ToJsonElement(),
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("List pages discovered by an Olostep crawl, with optional pagination and relevance sorting.")]
    [McpServerTool(Title = "Olostep get crawl pages", Name = "olostep_crawl_pages", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Crawl_Pages(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Crawl identifier returned by Olostep crawl creation.")] string crawl_id,
        [Description("Optional cursor offset for pagination.")] int? cursor = null,
        [Description("Optional maximum number of page results to return.")] int? limit = null,
        [Description("Optional search query used to rank returned pages.")] string? search_query = null,
        [Description("Optional deprecated formats list as newline-separated or comma-separated values such as html or markdown.")] string? formats = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepGetCrawlPagesRequest
                {
                    CrawlId = crawl_id,
                    Cursor = cursor,
                    Limit = limit,
                    SearchQuery = search_query,
                    Formats = formats
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.CrawlId);

            var query = new Dictionary<string, string?>
            {
                ["cursor"] = typed.Cursor?.ToString(),
                ["limit"] = typed.Limit?.ToString(),
                ["search_query"] = typed.SearchQuery,
                ["formats"] = typed.Formats is null ? null : string.Join(",", OlostepHelpers.ParseDelimitedList(typed.Formats) ?? [])
            };

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.GetJsonAsync($"v1/crawls/{Uri.EscapeDataString(typed.CrawlId)}/pages", query, cancellationToken) ?? new JsonObject();
            var pagesCount = response["pages_count"]?.GetValue<int?>() ?? OlostepHelpers.CountArray(response["pages"]);
            var nextCursor = response["cursor"]?.GetValue<int?>();
            var summary = $"Olostep crawl pages retrieved. CrawlId={typed.CrawlId}. Pages={pagesCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/crawls/{crawl_id}/pages",
                    new
                    {
                        crawl_id = typed.CrawlId,
                        cursor = typed.Cursor,
                        limit = typed.Limit,
                        search_query = typed.SearchQuery,
                        formats = OlostepHelpers.ParseDelimitedList(typed.Formats)
                    },
                    response,
                    ("crawlId", response["crawl_id"]?.GetValue<string>() ?? typed.CrawlId),
                    ("status", OlostepHelpers.GetString(response, "status")),
                    ("pagesCount", pagesCount),
                    ("cursor", nextCursor)).ToJsonElement(),
                Content = [summary.ToTextContentBlock()]
            };
        });
}

public sealed class OlostepCreateCrawlRequest
{
    [JsonPropertyName("start_url")]
    [Required]
    [Url]
    [Description("Starting URL for the crawl.")]
    public string StartUrl { get; set; } = string.Empty;

    [JsonPropertyName("max_pages")]
    [Range(1, int.MaxValue)]
    [Description("Maximum number of pages to crawl.")]
    public int MaxPages { get; set; }

    [JsonPropertyName("include_urls")]
    [Description("Include path patterns as a newline-separated or comma-separated string.")]
    public string? IncludeUrls { get; set; }

    [JsonPropertyName("exclude_urls")]
    [Description("Exclude path patterns as a newline-separated or comma-separated string.")]
    public string? ExcludeUrls { get; set; }

    [JsonPropertyName("max_depth")]
    [Range(1, int.MaxValue)]
    [Description("Optional maximum crawl depth.")]
    public int? MaxDepth { get; set; }

    [JsonPropertyName("include_external")]
    [Description("Include first-degree external links.")]
    public bool IncludeExternal { get; set; }

    [JsonPropertyName("include_subdomain")]
    [Description("Include subdomains of the target website.")]
    public bool IncludeSubdomain { get; set; }

    [JsonPropertyName("search_query")]
    [Description("Optional search query used to prioritize relevant links.")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("top_n")]
    [Range(1, int.MaxValue)]
    [Description("Optional top N links to crawl per page when using search_query.")]
    public int? TopN { get; set; }

    [JsonPropertyName("webhook")]
    [Url]
    [Description("Optional webhook URL to receive crawl completion notifications.")]
    public string? Webhook { get; set; }

    [JsonPropertyName("timeout")]
    [Range(1, int.MaxValue)]
    [Description("Optional crawl timeout in seconds.")]
    public int? Timeout { get; set; }

    [JsonPropertyName("follow_robots_txt")]
    [Description("Respect robots.txt rules during crawling.")]
    public bool FollowRobotsTxt { get; set; } = true;

    [JsonPropertyName("scrape_formats")]
    [Description("Optional scrape formats as a newline-separated or comma-separated string.")]
    public string? ScrapeFormats { get; set; }

    [JsonPropertyName("scrape_parser")]
    [Description("Optional Olostep parser name applied to each crawled page.")]
    public string? ScrapeParser { get; set; }

    [JsonPropertyName("wait_for_completion")]
    [Description("Wait for crawl completion by polling crawl status.")]
    public bool WaitForCompletion { get; set; }

    [JsonPropertyName("poll_interval_seconds")]
    [Range(1, 300)]
    [Description("Polling interval in seconds when waiting for completion.")]
    public int PollIntervalSeconds { get; set; } = 5;

    [JsonPropertyName("max_wait_seconds")]
    [Range(1, 7200)]
    [Description("Maximum seconds to wait when waiting for completion.")]
    public int MaxWaitSeconds { get; set; } = 600;
}

public sealed class OlostepGetCrawlRequest
{
    [JsonPropertyName("crawl_id")]
    [Required]
    [Description("Crawl identifier returned by Olostep crawl creation.")]
    public string CrawlId { get; set; } = string.Empty;
}

public sealed class OlostepGetCrawlPagesRequest
{
    [JsonPropertyName("crawl_id")]
    [Required]
    [Description("Crawl identifier returned by Olostep crawl creation.")]
    public string CrawlId { get; set; } = string.Empty;

    [JsonPropertyName("cursor")]
    [Range(0, int.MaxValue)]
    [Description("Optional cursor offset for pagination.")]
    public int? Cursor { get; set; }

    [JsonPropertyName("limit")]
    [Range(1, int.MaxValue)]
    [Description("Optional maximum number of page results to return.")]
    public int? Limit { get; set; }

    [JsonPropertyName("search_query")]
    [Description("Optional search query used to rank returned pages.")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("formats")]
    [Description("Optional deprecated formats list as a newline-separated or comma-separated string.")]
    public string? Formats { get; set; }
}
