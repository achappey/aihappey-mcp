using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ScrapeDrive;

public static class ScrapeDriveService
{
    [Description("Scrape a webpage with ScrapeDrive async scraping, poll until completion, and return the final result as structured content.")]
    [McpServerTool(Title = "ScrapeDrive scrape", Name = "scrapedrive_scrape", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ScrapeDrive_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL to scrape. Must be HTTP or HTTPS.")] string url,
        [Description("Proxy tier: standard, advanced, or hyperdrive.")] string scrapeTier = "standard",
        [Description("ISO 3166-1 alpha-2 country code.")] string? countryCode = null,
        [Description("Custom proxy in format http(s):user:pass@host:port. Overrides scrape tier.")] string? customProxy = null,
        [Description("Sticky session number. Same number = same proxy IP.")] int? sessionNumber = null,
        [Description("Use headless browser with JavaScript execution.")] bool renderJs = true,
        [Description("Device emulation: desktop or mobile.")] string deviceType = "desktop",
        [Description("Browser navigation wait strategy: domcontentloaded, load, or networkidle.")] string? waitBrowser = null,
        [Description("CSS selector to wait for before completing the scrape.")] string? waitFor = null,
        [Description("Fixed delay in milliseconds after wait_for. Range: 0-30000.")][Range(0, 30000)] int? waitMs = null,
        [Description("Block images, CSS, fonts, and tracking scripts.")] bool blockResources = true,
        [Description("Block advertisement scripts and content.")] bool blockAds = false,
        [Description("Forward sdrive--prefixed headers to the target, with the prefix stripped.")] bool forwardSdriveHeaders = false,
        [Description("Maximum request timeout in milliseconds. Range: 10000-130000.")][Range(10000, 130000)] int timeoutMs = 130000,
        [Description("Output format: html, page_text, or page_markdown.")] string resultType = "html",
        [Description("Capture viewport screenshot as PNG and return screenshot_url when available.")] bool screenshot = false,
        [Description("Capture full page screenshot as PNG.")] bool screenshotFullpage = false,
        [Description("Capture a specific element by CSS selector as PNG.")] string? screenshotSelector = null,
        [Description("Polling interval in seconds while waiting for completion.")][Range(1, 30)] int pollIntervalSeconds = 2,
        [Description("Maximum wait time in seconds for the async scrape to complete.")][Range(5, 900)] int pollTimeoutSeconds = 180,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);
                ValidateUrl(url);

                var client = serviceProvider.GetRequiredService<ScrapeDriveClient>();

                var payload = new JsonObject
                {
                    ["url"] = url,
                    ["scrape_tier"] = scrapeTier,
                    ["country_code"] = countryCode,
                    ["custom_proxy"] = customProxy,
                    ["session_number"] = sessionNumber.HasValue ? JsonValue.Create(sessionNumber.Value) : null,
                    ["render_js"] = renderJs,
                    ["device_type"] = deviceType,
                    ["wait_browser"] = waitBrowser,
                    ["wait_for"] = waitFor,
                    ["wait_ms"] = waitMs.HasValue ? JsonValue.Create(waitMs.Value) : null,
                    ["block_resources"] = blockResources,
                    ["block_ads"] = blockAds,
                    ["forward_sdrive_headers"] = forwardSdriveHeaders,
                    ["timeout_ms"] = timeoutMs,
                    ["result_type"] = resultType,
                    ["screenshot"] = screenshot,
                    ["screenshot_fullpage"] = screenshotFullpage,
                    ["screenshot_selector"] = screenshotSelector
                };

                RemoveNulls(payload);

                var created = await client.PostJsonAsync("scrape/async", payload, cancellationToken);
                var initialBody = created.Body as JsonObject ?? [];
                var jobId = initialBody["id"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(jobId))
                    throw new Exception("ScrapeDrive async response did not include a job id.");

                JsonNode? jobState = null;
                JsonObject? finalResult = null;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(pollTimeoutSeconds));

                while (!timeoutCts.IsCancellationRequested)
                {
                    jobState = await client.GetJsonAsync($"job/{Uri.EscapeDataString(jobId)}", timeoutCts.Token);
                    var job = jobState as JsonObject ?? [];
                    var status = job["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();

                    if (status == "completed")
                    {
                        finalResult = job;
                        break;
                    }

                    if (status == "failed")
                    {
                        var error = ExtractError(job) ?? "ScrapeDrive job failed.";
                        throw new Exception(error);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
                }

                if (finalResult is null)
                    throw new TimeoutException($"ScrapeDrive polling timed out after {pollTimeoutSeconds} seconds for job {jobId}.");

                var structured = new JsonObject
                {
                    ["provider"] = "scrapedrive",
                    ["baseUrl"] = "https://api.scrapedrive.com/api/v1",
                    ["endpoint"] = "/api/v1/scrape/async",
                    ["request"] = payload.DeepClone(),
                    ["initialResponse"] = initialBody.DeepClone(),
                    ["jobId"] = jobId,
                    ["job"] = finalResult.DeepClone(),
                    ["status"] = finalResult["status"]?.DeepClone(),
                    ["url"] = finalResult["url"]?.DeepClone(),
                    ["result"] = ExtractResult(finalResult),
                    ["screenshotUrl"] = finalResult["screenshot_url"]?.DeepClone(),
                    ["statusUrl"] = initialBody["status_url"]?.DeepClone()
                };

                RemoveNulls(structured);

                return structured;
            }));

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ValidationException("url must be a valid HTTP or HTTPS URL.");
    }

    private static JsonNode? ExtractResult(JsonObject job)
    {
        if (job["result"] is not null)
            return job["result"]!.DeepClone();

        var clone = job.DeepClone() as JsonObject ?? [];
        clone.Remove("id");
        clone.Remove("status");
        clone.Remove("status_url");
        return clone;
    }

    private static string? ExtractError(JsonObject job)
        => job["error"]?["message"]?.GetValue<string>()
           ?? job["error"]?["code"]?.GetValue<string>()
           ?? job["message"]?.GetValue<string>();

    private static void RemoveNulls(JsonObject obj)
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
                RemoveNulls(child);
                if (child.Count == 0)
                    obj.Remove(property.Key);
            }
        }
    }
}
