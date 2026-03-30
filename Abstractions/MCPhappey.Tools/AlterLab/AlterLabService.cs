using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AlterLab;

public static class AlterLabService
{
    [Description("Scrape a webpage with AlterLab. The tool waits for completion automatically and returns the final scrape result as structured content.")]
    [McpServerTool(Title = "AlterLab scrape", Name = "alterlab_scrape", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> AlterLab_Scrape(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The URL of the web page to scrape.")] string url,
        [Description("Scraping mode: auto, html, js, pdf, or ocr.")] string mode = "auto",
        [Description("Comma-separated output formats: text,json,html,markdown.")] string formatsCsv = "markdown,json",
        [Description("Enable JavaScript rendering.")] bool renderJs = false,
        [Description("Capture a screenshot.")] bool screenshot = false,
        [Description("Generate a PDF.")] bool generatePdf = false,
        [Description("Enable OCR.")] bool ocr = false,
        [Description("Use AlterLab proxy network.")] bool useProxy = false,
        [Description("Convert content to markdown in advanced options.")] bool markdown = true,
        [Description("Wait condition: domcontentloaded, networkidle, or load.")] string waitCondition = "networkidle",
        [Description("Maximum credits allowed for the request.")] double? maxCredits = null,
        [Description("Maximum tier allowed: 1, 2, 3, 4, or 5.")] string? maxTier = null,
        [Description("Prefer lower cost over speed.")] bool preferCost = false,
        [Description("Prefer speed over lower cost.")] bool preferSpeed = false,
        [Description("Fail fast instead of escalating tiers.")] bool failFast = false,
        [Description("Bypass cache and force a fresh scrape.")] bool forceRefresh = false,
        [Description("Include raw HTML in the response.")] bool includeRawHtml = false,
        [Description("AlterLab request timeout in seconds (1-300).")][Range(1, 300)] int timeoutSeconds = 30,
        [Description("Polling interval in seconds when AlterLab returns a job_id.")][Range(1, 30)] int pollIntervalSeconds = 2,
        [Description("Maximum wait time in seconds for polling async jobs.")][Range(5, 900)] int pollTimeoutSeconds = 180,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                var client = serviceProvider.GetRequiredService<AlterLabClient>();
                var formats = ParseCsv(formatsCsv);
                if (formats.Count == 0)
                    throw new ValidationException("At least one format is required.");

                var payload = new JsonObject
                {
                    ["url"] = url,
                    ["mode"] = mode,
                    ["sync"] = false,
                    ["formats"] = new JsonArray(formats.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                    ["advanced"] = new JsonObject
                    {
                        ["render_js"] = renderJs,
                        ["screenshot"] = screenshot,
                        ["generate_pdf"] = generatePdf,
                        ["ocr"] = ocr,
                        ["use_proxy"] = useProxy,
                        ["markdown"] = markdown,
                        ["wait_condition"] = waitCondition
                    },
                    ["cost_controls"] = new JsonObject
                    {
                        ["max_credits"] = maxCredits.HasValue ? JsonValue.Create(maxCredits.Value) : null,
                        ["max_tier"] = string.IsNullOrWhiteSpace(maxTier) ? null : JsonValue.Create(maxTier),
                        ["prefer_cost"] = preferCost,
                        ["prefer_speed"] = preferSpeed,
                        ["fail_fast"] = failFast
                    },
                    ["force_refresh"] = forceRefresh,
                    ["include_raw_html"] = includeRawHtml,
                    ["timeout"] = timeoutSeconds
                };

                RemoveNulls(payload);

                var created = await client.PostJsonAsync("scrape", payload, cancellationToken);
                var initialBody = created.Body as JsonObject ?? [];

                JsonNode? finalResult = null;
                JsonNode? jobState = null;
                string? jobId = null;

                if (created.StatusCode == 200)
                {
                    finalResult = initialBody;
                }
                else
                {
                    jobId = initialBody["job_id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(jobId))
                        throw new Exception("AlterLab returned 202 without a job_id.");

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(pollTimeoutSeconds));

                    while (!timeoutCts.IsCancellationRequested)
                    {
                        jobState = await client.GetJsonAsync($"jobs/{Uri.EscapeDataString(jobId)}", timeoutCts.Token);
                        var job = jobState as JsonObject ?? [];
                        var status = job["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();

                        if (status == "completed")
                        {
                            finalResult = job["result"]?.DeepClone() ?? job.DeepClone();
                            break;
                        }

                        if (status == "failed")
                        {
                            var error = job["error"]?.GetValue<string>() ?? "AlterLab job failed.";
                            throw new Exception(error);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
                    }

                    if (finalResult == null)
                        throw new TimeoutException($"AlterLab polling timed out after {pollTimeoutSeconds} seconds for job {jobId}.");
                }

                var resultObject = finalResult as JsonObject ?? [];
                var statusCode = resultObject["status_code"]?.GetValue<int?>();
                var title = resultObject["title"]?.GetValue<string>();
                var billing = resultObject["billing"]?.DeepClone();

                var structured = new JsonObject
                {
                    ["provider"] = "alterlab",
                    ["baseUrl"] = "https://api.alterlab.io/api/v1",
                    ["endpoint"] = "/api/v1/scrape",
                    ["request"] = payload.DeepClone(),
                    ["initialResponse"] = initialBody.DeepClone(),
                    ["job"] = jobState?.DeepClone(),
                    ["jobId"] = jobId,
                    ["result"] = resultObject.DeepClone(),
                    ["statusCode"] = statusCode,
                    ["title"] = title,
                    ["billing"] = billing
                };

                RemoveNulls(structured);

                var summary = $"AlterLab scrape completed. Url={url}. StatusCode={statusCode?.ToString() ?? created.StatusCode.ToString()}.";

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = (structured).ToJsonElement(),
                    Content = [summary.ToTextContentBlock()]
                };
            }));

    private static List<string> ParseCsv(string csv)
        => csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
