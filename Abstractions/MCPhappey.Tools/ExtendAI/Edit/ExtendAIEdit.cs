using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.ExtendAI.Edit;

public static class ExtendAIEdit
{
    [Description("Edit a file with Extend AI (async), wait for completion, upload edited file output, return structured content, then delete run and uploaded file.")]
    [McpServerTool(Title = "Extend AI edit file", Name = "extendai_edit_file", ReadOnly = true)]
    public static async Task<CallToolResult?> ExtendAI_Edit_File(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint/OneDrive/HTTP) to edit.")]
        string fileUrl,
        [Description("Instructions for the edit operation.")]
        string instructions,
        [Description("Whether to flatten the PDF (form widgets will not be editable). Defaults to true.")]
        bool flattenPdf = true,
        [Description("Whether to parse table regions as arrays of objects. Defaults to false.")]
        bool tableParsingEnabled = false,
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

                if (string.IsNullOrWhiteSpace(instructions))
                    throw new ArgumentException("instructions is required.");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var extendFiles = serviceProvider.GetRequiredService<ExtendAIFileService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var inputFile = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var uploadedFileId = string.Empty;
                var editRunId = string.Empty;
                var editedFileId = string.Empty;

                try
                {
                    uploadedFileId = await extendFiles.UploadAsync(inputFile, cancellationToken);

                    var createBody = new JsonObject
                    {
                        ["file"] = new JsonObject
                        {
                            ["id"] = uploadedFileId
                        },
                        ["config"] = new JsonObject
                        {
                            ["instructions"] = instructions,
                            ["advancedOptions"] = new JsonObject
                            {
                                ["flattenPdf"] = flattenPdf,
                                ["tableParsingEnabled"] = tableParsingEnabled
                            }
                        }
                    };

                    var createResponse = await extendFiles.CreateEditRunAsync(createBody, cancellationToken);
                    editRunId = createResponse["id"]?.GetValue<string>()
                        ?? throw new Exception("Extend edit run response missing id.");

                    var sw = Stopwatch.StartNew();
                    JsonNode? latest = createResponse;

                    while (true)
                    {
                        if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                            throw new TimeoutException($"Extend edit run timed out after {maxWaitSeconds}s.");

                        latest = await extendFiles.GetEditRunAsync(editRunId, cancellationToken);
                        var status = latest?["status"]?.GetValue<string>()?.ToUpperInvariant();

                        if (status == "PROCESSED")
                            break;

                        if (status == "FAILED")
                        {
                            var failureReason = latest?["failureReason"]?.GetValue<string>();
                            var failureMessage = latest?["failureMessage"]?.GetValue<string>();
                            throw new Exception($"Extend edit failed: {failureReason ?? "unknown"}. {failureMessage}".Trim());
                        }

                        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                    }

                    var links = new List<ResourceLinkBlock>();
                    editedFileId = latest?["output"]?["editedFile"]?["id"]?.GetValue<string>() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(editedFileId))
                    {
                        var fileItem = await extendFiles.DownloadFileAsync(editedFileId, cancellationToken);
                        var baseName = requestContext.ToOutputFileName("edit");
                        var ext = Path.GetExtension(fileItem.Filename ?? string.Empty);
                        var resolvedExt = string.IsNullOrWhiteSpace(ext) ? string.Empty : ext;
                        var uploadName = string.IsNullOrWhiteSpace(resolvedExt)
                            ? baseName
                            : $"{baseName}{resolvedExt}";

                        var uploaded = await requestContext.Server.Upload(
                            serviceProvider,
                            uploadName,
                            fileItem.Contents,
                            cancellationToken);

                        if (uploaded != null)
                            links.Add(uploaded);
                    }

                    return new CallToolResult
                    {
                        StructuredContent = new JsonObject
                        {
                            ["editRunId"] = editRunId,
                            ["fileId"] = uploadedFileId,
                            ["editedFileId"] = string.IsNullOrWhiteSpace(editedFileId) ? null : editedFileId,
                            ["status"] = latest?["status"],
                            ["output"] = latest?["output"],
                            ["metrics"] = latest?["metrics"],
                            ["usage"] = latest?["usage"],
                            ["config"] = latest?["config"]
                        },
                        Content = links.Count > 0 ? [.. links] : []
                    };
                }
                finally
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(editRunId))
                            await extendFiles.DeleteEditRunAsync(editRunId, cancellationToken);
                    }
                    catch
                    {
                        // Ignore cleanup failure
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(editedFileId))
                            await extendFiles.DeleteFileAsync(editedFileId, cancellationToken);
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
