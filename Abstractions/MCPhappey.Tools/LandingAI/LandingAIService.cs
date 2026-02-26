using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.LandingAI;

public static class LandingAIService
{
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 900;

    [Description("Parse a document with LandingAI ADE async jobs, poll until terminal status, and return structured JSON result.")]
    [McpServerTool(Name = "ade_parse_wait", Title = "LandingAI ADE parse (wait)", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ADE_Parse_Wait(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Region endpoint selector: us or eu.")]
        string region = "us",
        [Description("Model version to use for parsing. Optional.")]
        string? model = null,
        [Description("Split mode. Use page to split by page. Optional.")]
        string? split = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var pollIntervalSeconds = DefaultPollIntervalSeconds;
                var maxWaitSeconds = DefaultMaxWaitSeconds;

                var normalizedRegion = NormalizeRegion(region);
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var inputFile = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

                using var client = serviceProvider.CreateLandingAIClient(normalizedRegion);

                var jobId = await CreateParseJobAsync(client, inputFile, model, split, cancellationToken);
                var finalStatus = await PollParseJobUntilTerminalAsync(client, jobId, pollIntervalSeconds, maxWaitSeconds, cancellationToken);

                return new JsonObject
                {
                    ["region"] = normalizedRegion,
                    ["job_id"] = jobId,
                    ["result"] = finalStatus
                };
            }));

    [Description("Extract structured data from Markdown using LandingAI ADE extract. Input markdown is loaded from fileUrl; schema is loaded from schemaFileUrl.")]
    [McpServerTool(Name = "ade_extract", Title = "LandingAI ADE extract", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ADE_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Markdown file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("JSON schema file URL (SharePoint/OneDrive/HTTPS).")]
        string schemaFileUrl,
        [Description("Region endpoint selector: us or eu.")]
        string region = "us",
        [Description("Model version to use for extraction. Optional.")]
        string? model = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");
                if (string.IsNullOrWhiteSpace(schemaFileUrl))
                    throw new ArgumentException("schemaFileUrl is required.");

                var normalizedRegion = NormalizeRegion(region);
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var markdown = await DownloadTextAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
                var schema = await DownloadAndNormalizeJsonAsync(serviceProvider, requestContext, downloadService, schemaFileUrl, cancellationToken);

                using var client = serviceProvider.CreateLandingAIClient(normalizedRegion);
                using var form = new MultipartFormDataContent();

                form.Add(new StringContent(markdown), "markdown");
                form.Add(new StringContent(schema), "schema");

                if (!string.IsNullOrWhiteSpace(model))
                    form.Add(new StringContent(model), "model");

                using var req = new HttpRequestMessage(HttpMethod.Post, "ade/extract") { Content = form };
                var result = await SendJsonAsync(client, req, cancellationToken, "ADE extract");

                return new JsonObject
                {
                    ["region"] = normalizedRegion,
                    ["result"] = result
                };
            }));

    [Description("Classify document sections with LandingAI ADE split. Input markdown is loaded from fileUrl and split classes are provided as JSON string in splitClassJson.")]
    [McpServerTool(Name = "ade_split", Title = "LandingAI ADE split", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ADE_Split(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Markdown file URL (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Split class configuration as JSON array string.")]
        string splitClassJson,
        [Description("Region endpoint selector: us or eu.")]
        string region = "us",
        [Description("Model version to use for split classification. Optional.")]
        string? model = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");
                if (string.IsNullOrWhiteSpace(splitClassJson))
                    throw new ArgumentException("splitClassJson is required.");

                var normalizedRegion = NormalizeRegion(region);
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                var markdown = await DownloadTextAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
                var splitClass = NormalizeJsonArray(splitClassJson, "splitClassJson");

                using var client = serviceProvider.CreateLandingAIClient(normalizedRegion);
                using var form = new MultipartFormDataContent();

                form.Add(new StringContent(markdown), "markdown");
                form.Add(new StringContent(splitClass), "split_class");

                if (!string.IsNullOrWhiteSpace(model))
                    form.Add(new StringContent(model), "model");

                using var req = new HttpRequestMessage(HttpMethod.Post, "ade/split") { Content = form };
                var result = await SendJsonAsync(client, req, cancellationToken, "ADE split");

                return new JsonObject
                {
                    ["region"] = normalizedRegion,
                    ["result"] = result
                };
            }));

    private static async Task<string> CreateParseJobAsync(
        HttpClient client,
        MCPhappey.Common.Models.FileItem file,
        string? model,
        string? split,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(file.Contents.ToArray());
        if (!string.IsNullOrWhiteSpace(file.MimeType))
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType);

        form.Add(fileContent, "document", file.Filename ?? "document.bin");

        if (!string.IsNullOrWhiteSpace(model))
            form.Add(new StringContent(model), "model");

        if (!string.IsNullOrWhiteSpace(split))
            form.Add(new StringContent(split), "split");

        using var req = new HttpRequestMessage(HttpMethod.Post, "ade/parse/jobs") { Content = form };
        var created = await SendJsonAsync(client, req, cancellationToken, "ADE parse jobs");

        var jobId = created?["job_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(jobId))
            throw new Exception("ADE parse jobs response did not include job_id.");

        return jobId;
    }

    private static async Task<JsonNode?> PollParseJobUntilTerminalAsync(
        HttpClient client,
        string jobId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        JsonNode? latest = null;

        while (true)
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                throw new TimeoutException($"ADE parse polling timed out after {maxWaitSeconds} seconds for job_id '{jobId}'.");

            using var req = new HttpRequestMessage(HttpMethod.Get, $"ade/parse/jobs/{Uri.EscapeDataString(jobId)}");
            latest = await SendJsonAsync(client, req, cancellationToken, "ADE get parse job");

            var status = latest?["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            if (status == "completed")
                return latest;

            if (status is "failed" or "cancelled")
            {
                var failure = latest?["failure_reason"]?.GetValue<string>();
                throw new Exception($"ADE parse job '{jobId}' ended with status '{status}'. Failure: {failure}");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken);
        }
    }

    private static async Task<JsonNode?> SendJsonAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        string operation)
    {
        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{operation} failed ({response.StatusCode}): {raw}");

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    private static string NormalizeRegion(string region)
    {
        var normalized = (region ?? "us").Trim().ToLowerInvariant();
        if (normalized is not "us" and not "eu")
            throw new ArgumentException("region must be 'us' or 'eu'.");
        return normalized;
    }

    private static string NormalizeJsonArray(string rawJson, string paramName)
    {
        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is not JsonArray array)
                throw new ArgumentException($"{paramName} must be a JSON array string.");
            return array.ToJsonString();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{paramName} is not valid JSON.", ex);
        }
    }

    private static async Task<MCPhappey.Common.Models.FileItem> DownloadSingleAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string url,
        CancellationToken cancellationToken)
    {
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        return files.FirstOrDefault() ?? throw new Exception($"No file content could be downloaded from: {url}");
    }

    private static async Task<string> DownloadTextAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var file = await DownloadSingleAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);
        var text = file.Contents.ToString();
        if (string.IsNullOrWhiteSpace(text))
            throw new Exception($"Downloaded file from '{fileUrl}' is empty.");

        return text;
    }

    private static async Task<string> DownloadAndNormalizeJsonAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var text = await DownloadTextAsync(serviceProvider, requestContext, downloadService, fileUrl, cancellationToken);

        try
        {
            var node = JsonNode.Parse(text)
                ?? throw new Exception($"JSON file at '{fileUrl}' is empty.");

            if (node is JsonObject obj)
                return obj.ToJsonString();

            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue))
            {
                var nested = JsonNode.Parse(stringValue) as JsonObject;
                if (nested is null)
                    throw new Exception($"JSON schema in '{fileUrl}' must resolve to a JSON object.");
                return nested.ToJsonString();
            }

            throw new Exception($"JSON schema in '{fileUrl}' must resolve to a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new Exception($"Invalid JSON in schemaFileUrl '{fileUrl}'.", ex);
        }
    }
}

