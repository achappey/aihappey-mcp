using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Tensorlake;

public static class TensorlakeDocuments
{
    [Description("Read a document with Tensorlake OCR. Downloads the file from fileUrl first, uploads it to Tensorlake, waits for parsing to finish, returns structured content, then deletes the parse job and uploaded file.")]
    [McpServerTool(Title = "Tensorlake read document", Name = "tensorlake_read_document", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Tensorlake_ReadDocument(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Document URL to read. Protected SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Optional page range, for example '1-3,5'. Empty means all pages.")] string pageRange = "",
        [Description("OCR model. Default: model03.")] string ocrModel = "model03",
        [Description("Chunking strategy: none, page, section, or fragment.")] string chunkingStrategy = "none",
        [Description("Table output mode: html or markdown.")] string tableOutputMode = "html",
        [Description("Include images in markdown output. Default: false.")] bool includeImages = false,
        [Description("Merge adjacent tables when possible. Default: false.")] bool mergeTables = false,
        [Description("Enable signature detection. Default: false.")] bool signatureDetection = false,
        [Description("Enable barcode detection. Default: false.")] bool barcodeDetection = false,
        [Description("Polling interval in seconds.")] int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds before timeout.")] int maxWaitSeconds = 900,
        [Description("When true, saves the OCR JSON result beside the source file using the same filename plus .LLMs.json when possible, otherwise falls back to the default MCP output location, and returns only a resource link.")] bool saveOutput = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            {
                var result = await ExecuteReadDocumentAsync(serviceProvider, requestContext, fileUrl, pageRange, ocrModel, chunkingStrategy, tableOutputMode, includeImages, mergeTables, signatureDetection, barcodeDetection, pollingIntervalSeconds, maxWaitSeconds, cancellationToken);
                if (saveOutput)
                    return await requestContext.SaveOutputAsync(serviceProvider, BinaryData.FromString(result?.ToJsonString() ?? "{}"), "json", cancellationToken: cancellationToken);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = result ?? new JsonObject()
                };
            });

    private static string NormalizeOption(string? input, string[] allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var value = input.Trim().ToLowerInvariant();
        return allowed.Contains(value) ? value : fallback;
    }

    private static string InferFileName(string fileUrl)
    {
        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return "document.bin";
    }

    private static async Task<JsonObject> ExecuteReadDocumentAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        string pageRange,
        string ocrModel,
        string chunkingStrategy,
        string tableOutputMode,
        bool includeImages,
        bool mergeTables,
        bool signatureDetection,
        bool barcodeDetection,
        int pollingIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ArgumentException("fileUrl is required.");

        pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
        maxWaitSeconds = Math.Max(30, maxWaitSeconds);

        ocrModel = NormalizeOption(ocrModel, ["model01", "model02", "model03", "gemini3", "model06"], "model03");
        chunkingStrategy = NormalizeOption(chunkingStrategy, ["none", "page", "section", "fragment"], "none");
        tableOutputMode = NormalizeOption(tableOutputMode, ["html", "markdown"], "html");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var tensorlake = serviceProvider.GetRequiredService<TensorlakeClient>();

        var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var inputFile = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

        var fileId = string.Empty;
        var parseId = string.Empty;

        try
        {
            var upload = await tensorlake.UploadFileAsync(
                inputFile.Contents.ToArray(),
                inputFile.Filename ?? InferFileName(fileUrl),
                inputFile.MimeType ?? "application/octet-stream",
                cancellationToken);

            fileId = upload["file_id"]?.GetValue<string>()
                ?? throw new Exception("Tensorlake upload response missing file_id.");

            var readRequest = new JsonObject
            {
                ["file_id"] = fileId,
                ["file_name"] = inputFile.Filename ?? InferFileName(fileUrl),
                ["parsing_options"] = new JsonObject
                {
                    ["ocr_model"] = ocrModel,
                    ["chunking_strategy"] = chunkingStrategy,
                    ["table_output_mode"] = tableOutputMode,
                    ["include_images"] = includeImages,
                    ["merge_tables"] = mergeTables,
                    ["signature_detection"] = signatureDetection,
                    ["barcode_detection"] = barcodeDetection
                },
                ["labels"] = new JsonObject
                {
                    ["source"] = "mcphappey",
                    ["tool"] = "readdocument",
                    ["fileUrl"] = fileUrl
                }
            };

            if (!string.IsNullOrWhiteSpace(pageRange))
                readRequest["page_range"] = pageRange.Trim();

            var created = await tensorlake.CreateReadJobAsync(readRequest, cancellationToken);
            parseId = created["parse_id"]?.GetValue<string>()
                ?? throw new Exception("Tensorlake read response missing parse_id.");

            var stopwatch = Stopwatch.StartNew();
            JsonObject? latest = null;

            while (true)
            {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                    throw new TimeoutException($"Tensorlake parse timed out after {maxWaitSeconds}s.");

                latest = await tensorlake.GetParseAsync(parseId, cancellationToken);
                var status = latest["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();

                if (status == "successful")
                    break;

                if (status == "failure")
                {
                    var error = latest["error"]?.GetValue<string>()
                        ?? latest["message_update"]?.GetValue<string>()
                        ?? "Tensorlake parse failed.";
                    throw new Exception(error);
                }

                await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
            }

            return new JsonObject
            {
                ["status"] = latest?["status"]?.GetValue<string>() ?? "successful",
                ["parseId"] = parseId,
                ["fileId"] = fileId,
                ["chunks"] = latest?["chunks"]?.DeepClone(),
                ["pages"] = latest?["pages"]?.DeepClone(),
                ["structuredData"] = latest?["structured_data"]?.DeepClone(),
                ["usage"] = latest?["usage"]?.DeepClone(),
                ["labels"] = latest?["labels"]?.DeepClone()
            };
        }
        finally
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(parseId))
                    await tensorlake.DeleteParseAsync(parseId, cancellationToken);
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(fileId))
                    await tensorlake.DeleteFileAsync(fileId, cancellationToken);
            }
            catch
            {
            }
        }
    }
}
