using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ExtendAI.Split;

public static class ExtendAISplit
{
    [Description("Split a file with Extend AI (async), wait for completion, upload split outputs to SharePoint/OneDrive, return structured results, then delete run and uploaded files.")]
    [McpServerTool(Title = "Extend AI split file", Name = "extendai_split_file", ReadOnly = true)]
    public static async Task<CallToolResult?> ExtendAI_Split_File(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint/OneDrive/HTTP) to split.")]
        string fileUrl,
        [Description("Splitter id to use.")]
        string splitterId,
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

                if (string.IsNullOrWhiteSpace(splitterId))
                    throw new ArgumentException("splitterId is required.");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var extendFiles = serviceProvider.GetRequiredService<ExtendAIFileService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var inputFile = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var uploadedFileId = string.Empty;
                var splitRunId = string.Empty;
                var splitFileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    uploadedFileId = await extendFiles.UploadAsync(inputFile, cancellationToken);

                    var createBody = new JsonObject
                    {
                        ["file"] = new JsonObject
                        {
                            ["id"] = uploadedFileId
                        },
                        ["splitter"] = new JsonObject
                        {
                            ["id"] = splitterId
                        }
                    };

                    var createResponse = await extendFiles.CreateSplitRunAsync(createBody, cancellationToken);
                    splitRunId = createResponse["id"]?.GetValue<string>()
                        ?? throw new Exception("Extend split run response missing id.");

                    var sw = Stopwatch.StartNew();
                    JsonNode? latest = createResponse;

                    while (true)
                    {
                        if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                            throw new TimeoutException($"Extend split run timed out after {maxWaitSeconds}s.");

                        latest = await extendFiles.GetSplitRunAsync(splitRunId, cancellationToken);
                        var status = latest?["status"]?.GetValue<string>()?.ToUpperInvariant();

                        if (status == "PROCESSED")
                            break;

                        if (status == "FAILED" || status == "CANCELLED")
                        {
                            var failureReason = latest?["failureReason"]?.GetValue<string>();
                            var failureMessage = latest?["failureMessage"]?.GetValue<string>();
                            throw new Exception($"Extend split failed: {failureReason ?? "unknown"}. {failureMessage}".Trim());
                        }

                        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                    }

                    var splitOutputs = latest?["output"]?["splits"]?.AsArray();
                    var links = new List<ResourceLinkBlock>();

                    if (splitOutputs != null)
                    {
                        var index = 0;
                        foreach (var split in splitOutputs)
                        {
                            index++;
                            var splitFileId = split?["fileId"]?.GetValue<string>();
                            if (string.IsNullOrWhiteSpace(splitFileId))
                                continue;

                            splitFileIds.Add(splitFileId);

                            var fileItem = await extendFiles.DownloadFileAsync(splitFileId, cancellationToken);
                            var baseName = requestContext.ToOutputFileName("split");
                            var ext = Path.GetExtension(fileItem.Filename ?? string.Empty);
                            var resolvedExt = string.IsNullOrWhiteSpace(ext) ? string.Empty : ext;
                            var uploadName = string.IsNullOrWhiteSpace(resolvedExt)
                                ? $"{baseName}-{index}"
                                : $"{baseName}-{index}{resolvedExt}";

                            var uploaded = await requestContext.Server.Upload(
                                serviceProvider,
                                uploadName,
                                fileItem.Contents,
                                cancellationToken);

                            if (uploaded != null)
                                links.Add(uploaded);
                        }
                    }

                    return new CallToolResult
                    {
                        StructuredContent = new JsonObject
                        {
                            ["splitRunId"] = splitRunId,
                            ["fileId"] = uploadedFileId,
                            ["status"] = latest?["status"],
                            ["output"] = latest?["output"],
                            ["usage"] = latest?["usage"],
                            ["config"] = latest?["config"],
                            ["splitter"] = latest?["splitter"],
                            ["splitterVersion"] = latest?["splitterVersion"],
                            ["metadata"] = latest?["metadata"],
                            ["dashboardUrl"] = latest?["dashboardUrl"]
                        },
                        Content = links.Count > 0 ? [.. links] : []
                    };
                }
                finally
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(splitRunId))
                            await extendFiles.DeleteSplitRunAsync(splitRunId, cancellationToken);
                    }
                    catch
                    {
                        // Ignore cleanup failure
                    }

                    foreach (var splitFileId in splitFileIds)
                    {
                        try
                        {
                            await extendFiles.DeleteFileAsync(splitFileId, cancellationToken);
                        }
                        catch
                        {
                            // Ignore cleanup failure
                        }
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
