using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ContextualAI.Parse;

public static class ContextualAIParse
{
    [Description("Parse a file by URL with Contextual AI and wait until the job is completed, then return parse results.")]
    [McpServerTool(Title = "Parse file and wait for result", 
        Name = "contextualai_parse_file", ReadOnly = true)]
    public static async Task<CallToolResult?> ContextualAI_Parse_File(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL to parse (supports SharePoint/OneDrive/public URLs).")]
        string fileUrl,
        [Description("Parse mode: basic or standard.")]
        string parseMode = "standard",
        [Description("Enable document hierarchy.")]
        bool enableDocumentHierarchy = false,
        [Description("Enable split tables.")]
        bool enableSplitTables = false,
        [Description("Threshold cells for split tables (0 disables explicit value).")]
        int maxSplitTableCells = 0,
        [Description("Optional page range (e.g. 0-2,5,6). Empty for all pages.")]
        string pageRange = "",
        [Description("Comma-separated output types: markdown-document,markdown-per-page,blocks-per-page")]
        string outputTypes = "markdown-document",
        [Description("Polling interval in seconds.")]
        int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds before timeout.")]
        int maxWaitSeconds = 900,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                parseMode = string.IsNullOrWhiteSpace(parseMode) ? "standard" : parseMode.Trim().ToLowerInvariant();
                if (parseMode is not "basic" and not "standard")
                    throw new ArgumentException("parseMode must be either 'basic' or 'standard'.");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var inputFile = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                using var client = serviceProvider.CreateContextualAIClient();

                var fileName = !string.IsNullOrWhiteSpace(inputFile.Filename)
                    ? inputFile.Filename!
                    : InferFilenameFromUrl(fileUrl);

                using var form = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(inputFile.Contents.ToArray());
                if (!string.IsNullOrWhiteSpace(inputFile.MimeType))
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(inputFile.MimeType);

                form.Add(fileContent, "raw_file", fileName);
                form.Add(new StringContent(parseMode), "parse_mode");
                form.Add(new StringContent(enableDocumentHierarchy ? "true" : "false"), "enable_document_hierarchy");
                form.Add(new StringContent(enableSplitTables ? "true" : "false"), "enable_split_tables");

                if (enableSplitTables && maxSplitTableCells > 0)
                    form.Add(new StringContent(maxSplitTableCells.ToString()), "max_split_table_cells");

                if (!string.IsNullOrWhiteSpace(pageRange))
                    form.Add(new StringContent(pageRange), "page_range");

                using var parseRequest = new HttpRequestMessage(HttpMethod.Post, "parse") { Content = form };
                using var parseResponse = await client.SendAsync(parseRequest, cancellationToken);
                var parseJson = await parseResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!parseResponse.IsSuccessStatusCode)
                    throw new Exception($"{parseResponse.StatusCode}: {parseJson}");

                using var parseDoc = JsonDocument.Parse(parseJson);
                var jobId = parseDoc.RootElement.TryGetProperty("job_id", out var j) ? j.GetString() : null;
                if (string.IsNullOrWhiteSpace(jobId))
                    throw new Exception("Contextual AI parse did not return a job_id.");

                var sw = Stopwatch.StartNew();
                string status = "pending";
                string statusPayload = string.Empty;

                while (true)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                        throw new TimeoutException($"Contextual AI parse timed out after {maxWaitSeconds}s. Last status: {status}");

                    using var statusReq = new HttpRequestMessage(HttpMethod.Get, $"parse/jobs/{Uri.EscapeDataString(jobId)}/status");
                    using var statusResp = await client.SendAsync(statusReq, cancellationToken);
                    statusPayload = await statusResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!statusResp.IsSuccessStatusCode)
                        throw new Exception($"{statusResp.StatusCode}: {statusPayload}");

                    using var statusDoc = JsonDocument.Parse(statusPayload);
                    status = statusDoc.RootElement.TryGetProperty("status", out var s)
                        ? (s.GetString() ?? "unknown").ToLowerInvariant()
                        : "unknown";

                    if (status == "completed")
                        break;

                    if (status is "failed" or "cancelled")
                        throw new Exception($"Contextual AI parse job ended with status '{status}'. Payload: {statusPayload}");

                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                }

                var types = outputTypes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant())
                    .Distinct()
                    .ToArray();

                if (types.Length == 0)
                    types = ["markdown-document"];

                var query = string.Join("&", types.Select(t => $"output_types={Uri.EscapeDataString(t)}"));
                var resultPath = $"parse/jobs/{Uri.EscapeDataString(jobId)}/results?{query}";

                using var resultReq = new HttpRequestMessage(HttpMethod.Get, resultPath);
                using var resultResp = await client.SendAsync(resultReq, cancellationToken);
                var resultJson = await resultResp.Content.ReadAsStringAsync(cancellationToken);

                if (!resultResp.IsSuccessStatusCode)
                    throw new Exception($"{resultResp.StatusCode}: {resultJson}");

                return new JsonObject
                {
                    ["job_id"] = jobId,
                    ["status"] = status,
                    ["results"] = JsonNode.Parse(resultJson)
                };
            }));

    private static string InferFilenameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? "document.bin" : name;
        }
        catch
        {
            return "document.bin";
        }
    }
}

