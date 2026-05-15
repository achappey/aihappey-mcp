using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WebCrawlerAPI;

public static class WebCrawlerAPIService
{
    [Description("Crawl a website with WebCrawlerAPI. The tool starts the async crawl, hides job polling, waits until the job is done or error, and returns the final job as structured content.")]
    [McpServerTool(Title = "WebCrawlerAPI crawl", Name = "webcrawlerapi_crawl", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> WebCrawlerAPI_Crawl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Seed URL where the crawler starts.")] string url,
        [Description("Maximum number of pages to crawl.")][Range(1, 10000)] int itemsLimit = 10,
        [Description("Comma-separated output formats: markdown, cleaned, html, links.")] string outputFormatsCsv = "markdown",
        [Description("Optional webhook URL called by WebCrawlerAPI when the task completes.")] string? webhookUrl = null,
        [Description("Extract only the main article/blog content.")] bool mainContentOnly = false,
        [Description("Optional regular expression to whitelist URLs.")] string? whitelistRegexp = null,
        [Description("Optional regular expression to blacklist URLs.")] string? blacklistRegexp = null,
        [Description("Respect the website robots.txt file.")] bool respectRobotsTxt = false,
        [Description("Optional maximum crawl depth from the starting URL. 0 crawls only the start page.")][Range(0, 100)] int? maxDepth = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            WebCrawlerAPIHelpers.ValidateRequired(url, nameof(url));

            var payload = new JsonObject
            {
                ["url"] = url.Trim(),
                ["output_formats"] = WebCrawlerAPIHelpers.ParseCsvArray(outputFormatsCsv, "markdown", nameof(outputFormatsCsv)),
                ["items_limit"] = itemsLimit,
                ["webhook_url"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(webhookUrl),
                ["main_content_only"] = mainContentOnly,
                ["whitelist_regexp"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(whitelistRegexp),
                ["blacklist_regexp"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(blacklistRegexp),
                ["respect_robots_txt"] = respectRobotsTxt,
                ["max_depth"] = maxDepth
            }.WithoutNulls();

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var created = await client.SendJsonAsync(HttpMethod.Post, "v1/crawl", payload, cancellationToken);
            var jobId = created?["id"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(jobId))
                throw new InvalidOperationException("WebCrawlerAPI crawl response did not include an id.");

            var finalJob = await WebCrawlerAPIHelpers.PollJobUntilTerminalAsync(client, jobId, cancellationToken);
            var status = finalJob?["status"]?.GetValue<string>() ?? "unknown";
            var itemCount = finalJob?["job_items"]?.AsArray()?.Count;
            var downloadedContent = await DownloadJobItemContentAsync(client, finalJob, cancellationToken);

            var structured = new JsonObject
            {
                ["provider"] = "webcrawlerapi",
                ["baseUrl"] = WebCrawlerAPIClient.BaseUrl,
                ["endpoint"] = "/v1/crawl",
                ["statusEndpoint"] = "/v1/job/{id}",
                ["request"] = payload.DeepClone(),
                ["initialResponse"] = created?.DeepClone(),
                ["jobId"] = jobId,
                ["status"] = status,
                ["result"] = finalJob?.DeepClone(),
                ["downloadedContent"] = downloadedContent
            }.WithoutNulls();

            var summary = $"WebCrawlerAPI crawl finished. JobId={jobId}. Status={status}. Items={itemCount?.ToString() ?? "unknown"}.";
            return await WebCrawlerAPIHelpers.CreateToolResultAsync(requestContext, structured, summary);
        });

    [Description("Scrape a single webpage with WebCrawlerAPI v2 and return all requested outputs as structured content.")]
    [McpServerTool(Title = "WebCrawlerAPI scrape", Name = "webcrawlerapi_scrape", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> WebCrawlerAPI_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the webpage to scrape.")] string url,
        [Description("Comma-separated output formats: markdown, cleaned, html, links.")] string outputFormatsCsv = "markdown",
        [Description("Optional prompt to run on scraped content to produce structured_data.")] string? prompt = null,
        [Description("Optional legacy single output format: markdown, cleaned, or html. Ignored when outputFormatsCsv is provided.")] string? outputFormat = null,
        [Description("Extract only the main article/blog content.")] bool mainContentOnly = false,
        [Description("Optional comma-separated CSS selectors to remove from output.")] string? cleanSelectors = null,
        [Description("Respect robots.txt and return an error if the URL is disallowed.")] bool respectRobotsTxt = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            WebCrawlerAPIHelpers.ValidateRequired(url, nameof(url));

            var payload = new JsonObject
            {
                ["url"] = url.Trim(),
                ["prompt"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(prompt),
                ["output_formats"] = WebCrawlerAPIHelpers.ParseCsvArray(outputFormatsCsv, "markdown", nameof(outputFormatsCsv)),
                ["output_format"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(outputFormat),
                ["main_content_only"] = mainContentOnly,
                ["clean_selectors"] = WebCrawlerAPIHelpers.NullIfWhiteSpace(cleanSelectors),
                ["respect_robots_txt"] = respectRobotsTxt
            }.WithoutNulls();

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var response = await client.SendJsonAsync(HttpMethod.Post, "v2/scrape", payload, cancellationToken);
            var success = response?["success"]?.GetValue<bool?>();
            var status = response?["status"]?.GetValue<string>() ?? (success == false ? "error" : "done");
            var pageTitle = response?["page_title"]?.GetValue<string>();

            var structured = WebCrawlerAPIHelpers.CreateStructuredResponse("/v2/scrape", payload, response);
            structured["success"] = success;
            structured["status"] = status;
            structured["pageTitle"] = pageTitle;
            structured.WithoutNulls();

            var summary = success == false
                ? $"WebCrawlerAPI scrape returned an application-level error. Status={status}. Error={response?["error_code"]?.GetValue<string>() ?? "unknown"}."
                : $"WebCrawlerAPI scrape completed. Status={status}. Title={pageTitle ?? "unknown"}.";

            return await WebCrawlerAPIHelpers.CreateToolResultAsync(requestContext, structured, summary);
        });

    [Description("Retrieve discovered URL clusters and URLs for an existing completed WebCrawlerAPI crawl job as structured content.")]
    [McpServerTool(Title = "WebCrawlerAPI job URLs", Name = "webcrawlerapi_job_urls", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> WebCrawlerAPI_Job_Urls(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Existing WebCrawlerAPI crawl job ID.")] string jobId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            WebCrawlerAPIHelpers.ValidateRequired(jobId, nameof(jobId));

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var response = await client.GetJsonAsync($"v1/job/{Uri.EscapeDataString(jobId)}/urls", cancellationToken);
            var urlsCount = response?["urls"]?.AsArray()?.Count;

            var structured = WebCrawlerAPIHelpers.CreateStructuredResponse(
                "/v1/job/{id}/urls",
                new JsonObject { ["jobId"] = jobId },
                response);

            return await WebCrawlerAPIHelpers.CreateToolResultAsync(
                requestContext,
                structured,
                $"WebCrawlerAPI job URLs retrieved. JobId={jobId}. Urls={urlsCount?.ToString() ?? "unknown"}.");
        });

    [Description("Download combined markdown content for an existing completed WebCrawlerAPI markdown crawl job and return the markdown directly as text plus structured content.")]
    [McpServerTool(Title = "WebCrawlerAPI job combined markdown", Name = "webcrawlerapi_job_markdown_content", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> WebCrawlerAPI_Job_Markdown_Content(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Existing completed WebCrawlerAPI crawl job ID created with markdown output.")] string jobId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            WebCrawlerAPIHelpers.ValidateRequired(jobId, nameof(jobId));

            var client = serviceProvider.GetRequiredService<WebCrawlerAPIClient>();
            var content = await client.GetTextAsync($"v1/job/{Uri.EscapeDataString(jobId)}/markdown/content", cancellationToken);

            var structured = new JsonObject
            {
                ["provider"] = "webcrawlerapi",
                ["baseUrl"] = WebCrawlerAPIClient.BaseUrl,
                ["endpoint"] = "/v1/job/{id}/markdown/content",
                ["request"] = new JsonObject { ["jobId"] = jobId },
                ["contentType"] = content.ContentType,
                ["statusCode"] = content.StatusCode,
                ["markdown"] = content.Text
            }.WithoutNulls();

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured.ToJsonElement(),
                Content = [content.Text.ToTextContentBlock()]
            };
        });

    private static async Task<JsonArray?> DownloadJobItemContentAsync(
        WebCrawlerAPIClient client,
        JsonNode? finalJob,
        CancellationToken cancellationToken)
    {
        if (finalJob?["status"]?.GetValue<string>() is not "done")
            return null;

        var items = finalJob?["job_items"]?.AsArray();
        if (items is null || items.Count == 0)
            return null;

        var result = new JsonArray();

        foreach (var item in items)
        {
            if (item is not JsonObject obj)
                continue;

            var content = new JsonObject
            {
                ["id"] = obj["id"]?.DeepClone(),
                ["originalUrl"] = obj["original_url"]?.DeepClone(),
                ["title"] = obj["title"]?.DeepClone(),
                ["status"] = obj["status"]?.DeepClone(),
                ["pageStatusCode"] = obj["page_status_code"]?.DeepClone()
            };

            await AddDownloadedTextAsync(client, content, "markdown", obj["markdown_content_url"]?.GetValue<string>(), cancellationToken);
            await AddDownloadedTextAsync(client, content, "cleaned", obj["cleaned_content_url"]?.GetValue<string>(), cancellationToken);
            await AddDownloadedTextAsync(client, content, "html", obj["raw_content_url"]?.GetValue<string>(), cancellationToken);

            if (obj["links"] is not null)
                content["links"] = obj["links"]?.DeepClone();

            result.Add(content.WithoutNulls());
        }

        return result;
    }

    private static async Task AddDownloadedTextAsync(
        WebCrawlerAPIClient client,
        JsonObject target,
        string propertyName,
        string? url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var content = await client.GetTextAsync(url, cancellationToken);
        target[propertyName] = new JsonObject
        {
            ["contentUrl"] = url,
            ["contentType"] = content.ContentType,
            ["statusCode"] = content.StatusCode,
            ["text"] = content.Text
        }.WithoutNulls();
    }
}
