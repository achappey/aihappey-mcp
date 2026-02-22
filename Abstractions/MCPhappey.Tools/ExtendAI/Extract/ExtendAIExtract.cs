using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ExtendAI.Extract;

public static class ExtendAIExtract
{
    [Description("Extract structured data with Extend AI (async), wait for completion, return output, then delete run and uploaded file.")]
    [McpServerTool(Title = "Extend AI extract file", Name = "extendai_extract_file", ReadOnly = true)]
    public static async Task<CallToolResult?> ExtendAI_Extract_File(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint/OneDrive/HTTP) to extract from.")]
        string fileUrl,
        [Description("Extractor id to use.")]
        string extractorId,
        [Description("Optional extractor version. Use 'latest' or a specific version string.")]
        string? extractorVersion = null,
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

                if (string.IsNullOrWhiteSpace(extractorId))
                    throw new ArgumentException("extractorId is required.");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var extendFiles = serviceProvider.GetRequiredService<ExtendAIFileService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var inputFile = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var uploadedFileId = string.Empty;
                var extractRunId = string.Empty;

                try
                {
                    uploadedFileId = await extendFiles.UploadAsync(inputFile, cancellationToken);

                    var extractorNode = new JsonObject
                    {
                        ["id"] = extractorId
                    };

                    if (!string.IsNullOrWhiteSpace(extractorVersion))
                        extractorNode["version"] = extractorVersion;

                    var createBody = new JsonObject
                    {
                        ["file"] = new JsonObject
                        {
                            ["id"] = uploadedFileId
                        },
                        ["extractor"] = extractorNode
                    };

                    var createResponse = await extendFiles.CreateExtractRunAsync(createBody, cancellationToken);
                    extractRunId = createResponse["id"]?.GetValue<string>()
                        ?? throw new Exception("Extend extract run response missing id.");

                    var sw = Stopwatch.StartNew();
                    JsonNode? latest = createResponse;

                    while (true)
                    {
                        if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                            throw new TimeoutException($"Extend extract run timed out after {maxWaitSeconds}s.");

                        latest = await extendFiles.GetExtractRunAsync(extractRunId, cancellationToken);
                        var status = latest?["status"]?.GetValue<string>()?.ToUpperInvariant();

                        if (status == "PROCESSED")
                            break;

                        if (status == "FAILED" || status == "CANCELLED")
                        {
                            var failureReason = latest?["failureReason"]?.GetValue<string>();
                            var failureMessage = latest?["failureMessage"]?.GetValue<string>();
                            throw new Exception($"Extend extract failed: {failureReason ?? "unknown"}. {failureMessage}".Trim());
                        }

                        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                    }

                    return new JsonObject
                    {
                        ["extractRunId"] = extractRunId,
                        ["fileId"] = uploadedFileId,
                        ["status"] = latest?["status"],
                        ["output"] = latest?["output"],
                        ["usage"] = latest?["usage"],
                        ["config"] = latest?["config"],
                        ["extractor"] = latest?["extractor"],
                        ["extractorVersion"] = latest?["extractorVersion"],
                        ["metadata"] = latest?["metadata"],
                        ["dashboardUrl"] = latest?["dashboardUrl"]
                    };
                }
                finally
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(extractRunId))
                            await extendFiles.DeleteExtractRunAsync(extractRunId, cancellationToken);
                    }
                    catch
                    {
                        // Ignore cleanup failure
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(uploadedFileId))
                            await extendFiles.DeleteFileAsync(uploadedFileId, cancellationToken);
                    }
                    catch
                    {
                        // Ignore cleanup failure
                    }
                }
            }));
}
