using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ExtendAI.Parse;

public static class ExtendAIParse
{
    [Description("Parse a file with Extend AI (async), wait for completion, return structured content, then delete run and uploaded file.")]
    [McpServerTool(Title = "Extend AI parse file", Name = "extendai_parse_file", ReadOnly = true)]
    public static async Task<CallToolResult?> ExtendAI_Parse_File(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint/OneDrive/HTTP) to parse.")]
        string fileUrl,
        [Description("Target format: markdown or spatial.")]
        string target = "markdown",
        [Description("Chunking strategy type: page, document, or section.")]
        string chunkingType = "page",
        [Description("Parsing engine: parse_performance or parse_light.")]
        string engine = "parse_performance",
        [Description("Optional page ranges, e.g. '1-2,5-6'. Empty for all pages.")]
        string pageRanges = "",
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

                target = NormalizeOption(target, ["markdown", "spatial"], "markdown");
                chunkingType = NormalizeOption(chunkingType, ["page", "document", "section"], "page");
                engine = NormalizeOption(engine, ["parse_performance", "parse_light"], "parse_performance");

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var extendFiles = serviceProvider.GetRequiredService<ExtendAIFileService>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var inputFile = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var uploadedFileId = string.Empty;
                var parseRunId = string.Empty;

                try
                {
                    uploadedFileId = await extendFiles.UploadAsync(inputFile, cancellationToken);

                    var config = BuildConfig(target, chunkingType, engine, pageRanges);

                    var createBody = new JsonObject
                    {
                        ["file"] = new JsonObject
                        {
                            ["id"] = uploadedFileId
                        },
                        ["config"] = config
                    };

                    var createResponse = await extendFiles.CreateParseRunAsync(createBody, cancellationToken);
                    parseRunId = createResponse["id"]?.GetValue<string>()
                        ?? throw new Exception("Extend parse run response missing id.");

                    var sw = Stopwatch.StartNew();
                    JsonNode? latest = createResponse;

                    while (true)
                    {
                        if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                            throw new TimeoutException($"Extend parse run timed out after {maxWaitSeconds}s.");

                        latest = await extendFiles.GetParseRunAsync(parseRunId, cancellationToken);
                        var status = latest?["status"]?.GetValue<string>()?.ToUpperInvariant();

                        if (status == "PROCESSED")
                            break;

                        if (status == "FAILED")
                        {
                            var failureReason = latest?["failureReason"]?.GetValue<string>();
                            var failureMessage = latest?["failureMessage"]?.GetValue<string>();
                            throw new Exception($"Extend parse failed: {failureReason ?? "unknown"}. {failureMessage}".Trim());
                        }

                        await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                    }

                    return new JsonObject
                    {
                        ["parseRunId"] = parseRunId,
                        ["fileId"] = uploadedFileId,
                        ["status"] = latest?["status"],
                        ["output"] = latest?["output"],
                        ["metrics"] = latest?["metrics"],
                        ["usage"] = latest?["usage"],
                        ["config"] = latest?["config"]
                    };
                }
                finally
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(parseRunId))
                            await extendFiles.DeleteParseRunAsync(parseRunId, cancellationToken);
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

    private static string NormalizeOption(string input, string[] allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var value = input.Trim().ToLowerInvariant();
        return allowed.Contains(value) ? value : fallback;
    }

    private static JsonObject BuildConfig(string target, string chunkingType, string engine, string pageRanges)
    {
        var config = new JsonObject
        {
            ["target"] = target,
            ["engine"] = engine,
            ["chunkingStrategy"] = new JsonObject
            {
                ["type"] = chunkingType
            }
        };

        var ranges = ParsePageRanges(pageRanges);
        if (ranges.Count > 0)
        {
            config["advancedOptions"] = new JsonObject
            {
                ["pageRanges"] = ranges
            };
        }

        return config;
    }

    private static JsonArray ParsePageRanges(string pageRanges)
    {
        var array = new JsonArray();

        if (string.IsNullOrWhiteSpace(pageRanges))
            return array;

        var segments = pageRanges
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var parts = segment.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length == 1 && int.TryParse(parts[0], out var single))
            {
                array.Add(new JsonObject
                {
                    ["start"] = single,
                    ["end"] = single
                });
                continue;
            }

            if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end))
            {
                if (end < start)
                    (start, end) = (end, start);

                array.Add(new JsonObject
                {
                    ["start"] = start,
                    ["end"] = end
                });
            }
        }

        return array;
    }
}
