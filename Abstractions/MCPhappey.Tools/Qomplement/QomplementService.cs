using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Qomplement;

public static class QomplementService
{
    [Description("Extract structured data from document fileUrl(s) with qomplement OCR. Supports SharePoint/OneDrive links and waits for async jobs automatically.")]
    [McpServerTool(
        Title = "Qomplement Extract Data",
        Name = "qomplement_extract",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Qomplement_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input file URL(s), comma/newline/semicolon separated. Supports SharePoint/OneDrive/HTTPS.")]
        string fileUrls,
        [Description("OCR model. Default: qomplement-OCR-v1. Alternative: qomplement-OCR-XL-v1.")]
        string model = "qomplement-OCR-v1",
        [Description("Optional schema JSON array string, e.g. [{\"name\":\"invoice_number\",\"type\":\"string\"}].")]
        string? schemaJson = null,
        [Description("Output format: json (default), csv, or xml.")]
        string outputFormat = "json",
        [Description("Include detailed extraction block.")]
        bool detail = false,
        [Description("Optional OCR chunk size for large documents.")]
        int? chunkSize = null,
        [Description("Chunk overlap in characters. Default: 0.")]
        int chunkOverlap = 0,
        [Description("Optional webhook URL.")]
        string? webhookUrl = null,
        [Description("Polling interval in seconds when async job is returned.")]
        [Range(1, 60)] int pollIntervalSeconds = 3,
        [Description("Maximum wait time in seconds for async polling.")]
        [Range(30, 3600)] int maxWaitSeconds = 900,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var typed = new QomplementExtractRequest
                {
                    FileUrls = fileUrls,
                    Model = model,
                    SchemaJson = schemaJson,
                    OutputFormat = outputFormat,
                    Detail = detail,
                    ChunkSize = chunkSize,
                    ChunkOverlap = chunkOverlap,
                    WebhookUrl = webhookUrl,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds
                };

                var (input, notAccepted, _) = await requestContext.Server.TryElicit(typed, cancellationToken);
                if (input == null) throw new ValidationException("No input data provided.");

                var urls = ParseUrls(input.FileUrls);
                if (urls.Count == 0)
                    throw new ValidationException("At least one fileUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var client = serviceProvider.GetRequiredService<QomplementClient>();

                using var form = new MultipartFormDataContent();
                foreach (var url in urls)
                {
                    var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                    var file = files.FirstOrDefault() ?? throw new InvalidOperationException($"Failed to download fileUrl: {url}");

                    var content = new ByteArrayContent(file.Contents.ToArray());
                    content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType)
                        ? "application/octet-stream"
                        : file.MimeType);
                    form.Add(content, "file", string.IsNullOrWhiteSpace(file.Filename) ? "input.bin" : file.Filename);
                }

                AddString(form, "model", input.Model);
                AddString(form, "schema", input.SchemaJson);
                AddString(form, "output_format", input.OutputFormat);
                AddString(form, "detail", input.Detail ? "true" : null);
                AddInt(form, "chunk_size", input.ChunkSize);
                AddString(form, "chunk_overlap", input.ChunkOverlap > 0 ? input.ChunkOverlap.ToString() : null);
                AddString(form, "webhook_url", input.WebhookUrl);

                var submit = await client.PostMultipartAsync("extract", form, cancellationToken) ?? new JsonObject();
                var job = await ResolveAndWaitIfNeededAsync(client, submit, input.PollIntervalSeconds, input.MaxWaitSeconds, requestContext, cancellationToken);

                return new JsonObject
                {
                    ["provider"] = "qomplement",
                    ["endpoint"] = "/v1/extract",
                    ["inputFileCount"] = urls.Count,
                    ["job"] = job
                };
            }));

    [Description("Fill a PDF from source fileUrl(s), instructions, or explicit mappings with qomplement. Waits for completion, uploads result to SharePoint/OneDrive, and returns resource link blocks.")]
    [McpServerTool(
        Title = "Qomplement Fill PDF",
        Name = "qomplement_fill_pdf",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Qomplement_FillPdf(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target PDF form file URL (SharePoint/OneDrive/HTTPS).")]
        string targetPdfUrl,
        [Description("Optional source file URL(s), comma/newline/semicolon separated.")]
        string? sourceFileUrls = null,
        [Description("Optional natural language instructions.")]
        string? instructions = null,
        [Description("Optional explicit JSON field mappings, e.g. {\"first_name\":\"John\"}.")]
        string? fieldMappingsJson = null,
        [Description("OCR model. Default: qomplement-OCR-v1.")]
        string model = "qomplement-OCR-v1",
        [Description("Flatten filled PDF (remove editable fields).")]
        bool flatten = false,
        [Description("Optional webhook URL.")]
        string? webhookUrl = null,
        [Description("Polling interval in seconds.")]
        [Range(1, 60)] int pollIntervalSeconds = 3,
        [Description("Maximum wait time in seconds.")]
        [Range(30, 3600)] int maxWaitSeconds = 900,
        [Description("Optional output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var typed = new QomplementFillPdfRequest
                {
                    TargetPdfUrl = targetPdfUrl,
                    SourceFileUrls = sourceFileUrls,
                    Instructions = instructions,
                    FieldMappingsJson = fieldMappingsJson,
                    Model = model,
                    Flatten = flatten,
                    WebhookUrl = webhookUrl,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("qomplement-pdf")
                };

                var (input, notAccepted, _) = await requestContext.Server.TryElicit(typed, cancellationToken);
                if (notAccepted != null) return notAccepted;
                if (input == null) throw new ValidationException("No input data provided.");

                ValidateFillPdfInput(input);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var client = serviceProvider.GetRequiredService<QomplementClient>();

                using var form = new MultipartFormDataContent();

                var targetFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, input.TargetPdfUrl, cancellationToken);
                var target = targetFiles.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download targetPdfUrl.");
                var targetContent = new ByteArrayContent(target.Contents.ToArray());
                targetContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(target.MimeType) ? "application/pdf" : target.MimeType);
                form.Add(targetContent, "target_pdf", string.IsNullOrWhiteSpace(target.Filename) ? "target.pdf" : target.Filename);

                var sourceUrls = ParseUrls(input.SourceFileUrls);
                foreach (var url in sourceUrls)
                {
                    var sourceFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                    var source = sourceFiles.FirstOrDefault() ?? throw new InvalidOperationException($"Failed to download source file: {url}");

                    var sourceContent = new ByteArrayContent(source.Contents.ToArray());
                    sourceContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(source.MimeType)
                        ? "application/octet-stream"
                        : source.MimeType);
                    form.Add(sourceContent, "source_files", string.IsNullOrWhiteSpace(source.Filename) ? "source.bin" : source.Filename);
                }

                AddString(form, "instructions", input.Instructions);
                AddString(form, "field_mappings", input.FieldMappingsJson);
                AddString(form, "model", input.Model);
                AddString(form, "flatten", input.Flatten ? "true" : null);
                AddString(form, "webhook_url", input.WebhookUrl);

                var submit = await client.PostMultipartAsync("fill/pdf", form, cancellationToken) ?? new JsonObject();
                var job = await WaitForFinalJobAsync(client, submit["id"]?.GetValue<string>(), input.PollIntervalSeconds, input.MaxWaitSeconds, requestContext, cancellationToken);

                var resource = await UploadJobDownloadAsync(
                    serviceProvider,
                    requestContext,
                    client,
                    job,
                    fallbackDownloadPath: $"jobs/{Uri.EscapeDataString(job["id"]?.GetValue<string>() ?? string.Empty)}/download",
                    uploadFileName: EnsureExtension(input.Filename, "pdf"),
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = new JsonObject
                    {
                        ["provider"] = "qomplement",
                        ["endpoint"] = "/v1/fill/pdf",
                        ["sourceFileCount"] = sourceUrls.Count,
                        ["job"] = job
                    }.ToJsonElement(),
                    Content = resource != null ? [resource] : []
                };
            }));

    [Description("Fill an Excel template from source fileUrl(s) with qomplement. Waits for completion, uploads result to SharePoint/OneDrive, and returns resource link blocks.")]
    [McpServerTool(
        Title = "Qomplement Fill Excel",
        Name = "qomplement_fill_excel",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Qomplement_FillExcel(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source file URL(s), comma/newline/semicolon separated.")]
        string sourceFileUrls,
        [Description("Target Excel template URL (.xlsx) in SharePoint/OneDrive/HTTPS.")]
        string targetExcelUrl,
        [Description("OCR model. Default: qomplement-OCR-v1.")]
        string model = "qomplement-OCR-v1",
        [Description("Fill mode: smart, complete, replace.")]
        string fillMode = "smart",
        [Description("Optional webhook URL.")]
        string? webhookUrl = null,
        [Description("Polling interval in seconds.")]
        [Range(1, 60)] int pollIntervalSeconds = 3,
        [Description("Maximum wait time in seconds.")]
        [Range(30, 3600)] int maxWaitSeconds = 900,
        [Description("Optional output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var typed = new QomplementFillExcelRequest
                {
                    SourceFileUrls = sourceFileUrls,
                    TargetExcelUrl = targetExcelUrl,
                    Model = model,
                    FillMode = fillMode,
                    WebhookUrl = webhookUrl,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("qomplement-excel")
                };

                var (input, notAccepted, _) = await requestContext.Server.TryElicit(typed, cancellationToken);
                if (notAccepted != null) return notAccepted;
                if (input == null) throw new ValidationException("No input data provided.");

                var sourceUrls = ParseUrls(input.SourceFileUrls);
                if (sourceUrls.Count == 0)
                    throw new ValidationException("At least one source fileUrl is required.");
                if (string.IsNullOrWhiteSpace(input.TargetExcelUrl))
                    throw new ValidationException("targetExcelUrl is required.");

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var client = serviceProvider.GetRequiredService<QomplementClient>();

                using var form = new MultipartFormDataContent();

                foreach (var url in sourceUrls)
                {
                    var sourceFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                    var source = sourceFiles.FirstOrDefault() ?? throw new InvalidOperationException($"Failed to download source file: {url}");

                    var sourceContent = new ByteArrayContent(source.Contents.ToArray());
                    sourceContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(source.MimeType)
                        ? "application/octet-stream"
                        : source.MimeType);
                    form.Add(sourceContent, "source_files", string.IsNullOrWhiteSpace(source.Filename) ? "source.bin" : source.Filename);
                }

                var targetFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, input.TargetExcelUrl, cancellationToken);
                var target = targetFiles.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download targetExcelUrl.");
                var targetContent = new ByteArrayContent(target.Contents.ToArray());
                targetContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(target.MimeType)
                    ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    : target.MimeType);
                form.Add(targetContent, "target_excel", string.IsNullOrWhiteSpace(target.Filename) ? "target.xlsx" : target.Filename);

                AddString(form, "model", input.Model);
                AddString(form, "fill_mode", input.FillMode);
                AddString(form, "webhook_url", input.WebhookUrl);

                var submit = await client.PostMultipartAsync("fill/excel", form, cancellationToken) ?? new JsonObject();
                var job = await WaitForFinalJobAsync(client, submit["id"]?.GetValue<string>(), input.PollIntervalSeconds, input.MaxWaitSeconds, requestContext, cancellationToken);

                var resource = await UploadJobDownloadAsync(
                    serviceProvider,
                    requestContext,
                    client,
                    job,
                    fallbackDownloadPath: $"jobs/{Uri.EscapeDataString(job["id"]?.GetValue<string>() ?? string.Empty)}/download",
                    uploadFileName: EnsureExtension(input.Filename, "xlsx"),
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = new JsonObject
                    {
                        ["provider"] = "qomplement",
                        ["endpoint"] = "/v1/fill/excel",
                        ["sourceFileCount"] = sourceUrls.Count,
                        ["job"] = job
                    }.ToJsonElement(),
                    Content = resource != null ? [resource] : []
                };
            }));

    private static async Task<JsonNode> ResolveAndWaitIfNeededAsync(
        QomplementClient client,
        JsonNode submit,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var status = submit["status"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            return submit;

        var id = submit["id"]?.GetValue<string>();
        return await WaitForFinalJobAsync(client, id, pollIntervalSeconds, maxWaitSeconds, requestContext, cancellationToken);
    }

    private static async Task<JsonNode> WaitForFinalJobAsync(
        QomplementClient client,
        string? jobId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new InvalidOperationException("Qomplement did not return a job id.");

        var startedAt = DateTimeOffset.UtcNow;
        var poll = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - startedAt > TimeSpan.FromSeconds(maxWaitSeconds))
                throw new TimeoutException($"Qomplement job '{jobId}' timed out after {maxWaitSeconds}s.");

            var job = await client.GetJsonAsync($"jobs/{Uri.EscapeDataString(jobId)}", cancellationToken) ?? new JsonObject();
            var status = job["status"]?.GetValue<string>() ?? string.Empty;
            poll++;

            await requestContext.Server.SendMessageNotificationAsync(
                $"Qomplement job {jobId}: status={status}, poll #{poll}",
                LoggingLevel.Info,
                cancellationToken);

            if (IsTerminalStatus(status))
            {
                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "error", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = job["error"]?["message"]?.GetValue<string>()
                        ?? job["error"]?.ToJsonString()
                        ?? "unknown error";
                    throw new InvalidOperationException($"Qomplement job '{jobId}' failed: {errorMessage}");
                }

                return job;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds)), cancellationToken);
        }
    }

    private static async Task<ResourceLinkBlock?> UploadJobDownloadAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        QomplementClient client,
        JsonNode job,
        string fallbackDownloadPath,
        string uploadFileName,
        CancellationToken cancellationToken)
    {
        var downloadUrl = job["result"]?["download_url"]?.GetValue<string>();
        var downloadPath = string.IsNullOrWhiteSpace(downloadUrl)
            ? fallbackDownloadPath
            : downloadUrl!;

        var (bytes, _) = await client.DownloadAsync(downloadPath, cancellationToken);
        if (bytes.Length == 0)
            throw new InvalidOperationException("Qomplement output download returned empty content.");

        return await requestContext.Server.Upload(
            serviceProvider,
            uploadFileName,
            BinaryData.FromBytes(bytes),
            cancellationToken);
    }

    private static bool IsTerminalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
               || status.Equals("done", StringComparison.OrdinalIgnoreCase)
               || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
               || status.Equals("error", StringComparison.OrdinalIgnoreCase)
               || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
               || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseUrls(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value
                .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void AddString(MultipartFormDataContent form, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            form.Add(new StringContent(value), key);
    }

    private static void AddInt(MultipartFormDataContent form, string key, int? value)
    {
        if (value.HasValue)
            form.Add(new StringContent(value.Value.ToString()), key);
    }

    private static string EnsureExtension(string filename, string extensionWithoutDot)
    {
        var ext = extensionWithoutDot.Trim().TrimStart('.');
        return filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";
    }

    private static void ValidateFillPdfInput(QomplementFillPdfRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPdfUrl))
            throw new ValidationException("targetPdfUrl is required.");

        var hasSource = ParseUrls(request.SourceFileUrls).Count > 0;
        var hasInstructions = !string.IsNullOrWhiteSpace(request.Instructions);
        var hasMappings = !string.IsNullOrWhiteSpace(request.FieldMappingsJson);

        if (!hasSource && !hasInstructions && !hasMappings)
            throw new ValidationException("At least one of sourceFileUrls, instructions, or fieldMappingsJson must be provided.");
    }

    [Description("Please confirm qomplement extract input.")]
    public sealed class QomplementExtractRequest
    {
        [Description("Input file URL(s), comma/newline/semicolon separated.")]
        [Required]
        public string FileUrls { get; set; } = default!;

        [Description("OCR model.")]
        public string Model { get; set; } = "qomplement-OCR-v1";

        [Description("Optional schema JSON array string.")]
        public string? SchemaJson { get; set; }

        [Description("Output format: json, csv, xml.")]
        public string OutputFormat { get; set; } = "json";

        [Description("Include detailed extraction data.")]
        public bool Detail { get; set; }

        [Description("Optional chunk size.")]
        public int? ChunkSize { get; set; }

        [Description("Chunk overlap.")]
        public int ChunkOverlap { get; set; }

        [Description("Optional webhook URL.")]
        public string? WebhookUrl { get; set; }

        [Range(1, 60)]
        [Description("Polling interval seconds.")]
        public int PollIntervalSeconds { get; set; } = 3;

        [Range(30, 3600)]
        [Description("Maximum wait seconds.")]
        public int MaxWaitSeconds { get; set; } = 900;
    }

    [Description("Please confirm qomplement fill PDF input.")]
    public sealed class QomplementFillPdfRequest
    {
        [Description("Target PDF URL.")]
        [Required]
        public string TargetPdfUrl { get; set; } = default!;

        [Description("Optional source file URLs.")]
        public string? SourceFileUrls { get; set; }

        [Description("Optional natural language instructions.")]
        public string? Instructions { get; set; }

        [Description("Optional JSON field mappings.")]
        public string? FieldMappingsJson { get; set; }

        [Description("OCR model.")]
        public string Model { get; set; } = "qomplement-OCR-v1";

        [Description("Flatten output PDF.")]
        public bool Flatten { get; set; }

        [Description("Optional webhook URL.")]
        public string? WebhookUrl { get; set; }

        [Range(1, 60)]
        [Description("Polling interval seconds.")]
        public int PollIntervalSeconds { get; set; } = 3;

        [Range(30, 3600)]
        [Description("Maximum wait seconds.")]
        public int MaxWaitSeconds { get; set; } = 900;

        [Description("Output filename base.")]
        [Required]
        public string Filename { get; set; } = default!;
    }

    [Description("Please confirm qomplement fill Excel input.")]
    public sealed class QomplementFillExcelRequest
    {
        [Description("Source file URL(s).")]
        [Required]
        public string SourceFileUrls { get; set; } = default!;

        [Description("Target Excel template URL.")]
        [Required]
        public string TargetExcelUrl { get; set; } = default!;

        [Description("OCR model.")]
        public string Model { get; set; } = "qomplement-OCR-v1";

        [Description("Fill mode: smart, complete, replace.")]
        public string FillMode { get; set; } = "smart";

        [Description("Optional webhook URL.")]
        public string? WebhookUrl { get; set; }

        [Range(1, 60)]
        [Description("Polling interval seconds.")]
        public int PollIntervalSeconds { get; set; } = 3;

        [Range(30, 3600)]
        [Description("Maximum wait seconds.")]
        public int MaxWaitSeconds { get; set; } = 900;

        [Description("Output filename base.")]
        [Required]
        public string Filename { get; set; } = default!;
    }
}

