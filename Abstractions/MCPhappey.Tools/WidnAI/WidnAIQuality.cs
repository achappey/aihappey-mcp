using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WidnAI;

public static class WidnAIQuality
{
    [Description("Evaluate translation quality by comparing sourceText + targetText against a referenceText using WidnAI /quality/evaluate.")]
    [McpServerTool(
        Name = "widnai_quality_evaluate_text",
        Title = "WidnAI quality evaluate (text)",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Quality_EvaluateText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source text.")] string sourceText,
        [Description("Translated text to evaluate.")] string targetText,
        [Description("Reference translation text.")] string referenceText,
        [Description("Evaluation model. Only xcomet-xl is supported.")] string model = "xcomet-xl",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var normalizedModel = NormalizeEvaluateModel(model);

            ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetText);
            ArgumentException.ThrowIfNullOrWhiteSpace(referenceText);

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var requestBody = new
            {
                segments = new[]
                {
                    new
                    {
                        sourceText,
                        targetText,
                        referenceText
                    }
                },
                model = normalizedModel
            };

            var raw = await widn.PostJsonAsync("quality/evaluate", requestBody, cancellationToken) ?? new JsonObject();
            var structured = BuildStructuredResult(
                operation: "evaluate",
                model: normalizedModel,
                sourceText: sourceText,
                targetText: targetText,
                referenceText: referenceText,
                tone: null,
                targetLocale: null,
                raw: raw);

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
                ]
            };
        });

    [Description("Evaluate translation quality with WidnAI /quality/evaluate by scraping referenceText from fileUrl and combining it with sourceText + targetText.")]
    [McpServerTool(
        Name = "widnai_quality_evaluate_file",
        Title = "WidnAI quality evaluate (file)",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Quality_EvaluateFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source text.")] string sourceText,
        [Description("Translated text to evaluate.")] string targetText,
        [Description("File URL containing reference translation text (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Evaluation model. Only xcomet-xl is supported.")] string model = "xcomet-xl",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var normalizedModel = NormalizeEvaluateModel(model);

            ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetText);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var referenceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(referenceText))
                throw new InvalidOperationException("No readable reference text content found in fileUrl.");

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var requestBody = new
            {
                segments = new[]
                {
                    new
                    {
                        sourceText,
                        targetText,
                        referenceText
                    }
                },
                model = normalizedModel
            };

            var raw = await widn.PostJsonAsync("quality/evaluate", requestBody, cancellationToken) ?? new JsonObject();
            var structured = BuildStructuredResult(
                operation: "evaluate",
                model: normalizedModel,
                sourceText: sourceText,
                targetText: targetText,
                referenceText: referenceText,
                tone: null,
                targetLocale: null,
                raw: raw);

            structured["input"]!["referenceFromFileUrl"] = fileUrl;

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
                ]
            };
        });

    [Description("Estimate translation quality from sourceText + targetText using WidnAI /quality/estimate.")]
    [McpServerTool(
        Name = "widnai_quality_estimate_text",
        Title = "WidnAI quality estimate (text)",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Quality_EstimateText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source text.")] string sourceText,
        [Description("Translated text to estimate quality for.")] string targetText,
        [Description("Target locale (e.g., nl-NL, en-US).")]
        string targetLocale,
        [Description("Optional tone (e.g., formal, casual).")]
        string? tone = null,
        [Description("Model: mqm-qe or xcomet-xl. Default: mqm-qe.")] string model = "mqm-qe",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var normalizedModel = NormalizeEstimateModel(model);

            ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetText);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var requestBody = new
            {
                segments = new[]
                {
                    new
                    {
                        sourceText,
                        targetText,
                        tone = NormalizeOptional(tone),
                        targetLocale
                    }
                },
                model = normalizedModel
            };

            var raw = await widn.PostJsonAsync("quality/estimate", requestBody, cancellationToken) ?? new JsonObject();
            var structured = BuildStructuredResult(
                operation: "estimate",
                model: normalizedModel,
                sourceText: sourceText,
                targetText: targetText,
                referenceText: null,
                tone: NormalizeOptional(tone),
                targetLocale: targetLocale,
                raw: raw);

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
                ]
            };
        });

    [Description("Estimate translation quality with WidnAI /quality/estimate by scraping targetText from fileUrl and combining it with sourceText.")]
    [McpServerTool(
        Name = "widnai_quality_estimate_file",
        Title = "WidnAI quality estimate (file)",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> WidnAI_Quality_EstimateFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source text.")] string sourceText,
        [Description("File URL containing translated target text (SharePoint/OneDrive/HTTPS).")]
        string fileUrl,
        [Description("Target locale (e.g., nl-NL, en-US).")]
        string targetLocale,
        [Description("Optional tone (e.g., formal, casual).")]
        string? tone = null,
        [Description("Model: mqm-qe or xcomet-xl. Default: mqm-qe.")] string model = "mqm-qe",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var normalizedModel = NormalizeEstimateModel(model);

            ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var targetText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(targetText))
                throw new InvalidOperationException("No readable target text content found in fileUrl.");

            var widn = serviceProvider.GetRequiredService<WidnAIClient>();
            var requestBody = new
            {
                segments = new[]
                {
                    new
                    {
                        sourceText,
                        targetText,
                        tone = NormalizeOptional(tone),
                        targetLocale
                    }
                },
                model = normalizedModel
            };

            var raw = await widn.PostJsonAsync("quality/estimate", requestBody, cancellationToken) ?? new JsonObject();
            var structured = BuildStructuredResult(
                operation: "estimate",
                model: normalizedModel,
                sourceText: sourceText,
                targetText: targetText,
                referenceText: null,
                tone: NormalizeOptional(tone),
                targetLocale: targetLocale,
                raw: raw);

            structured["input"]!["targetFromFileUrl"] = fileUrl;

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true }).ToTextContentBlock()
                ]
            };
        });

    private static JsonObject BuildStructuredResult(
        string operation,
        string model,
        string sourceText,
        string targetText,
        string? referenceText,
        string? tone,
        string? targetLocale,
        JsonNode raw)
    {
        var rawSegments = raw["segments"] as JsonArray;
        var normalizedSegments = new JsonArray();

        if (rawSegments != null)
        {
            foreach (var item in rawSegments)
            {
                if (item is not JsonObject segmentObj)
                    continue;

                var outSegment = new JsonObject
                {
                    ["score"] = segmentObj["score"]?.GetValue<double?>(),
                    ["errorSpans"] = NormalizeErrorSpans(segmentObj["errorSpans"] as JsonArray)
                };

                normalizedSegments.Add(outSegment);
            }
        }

        return new JsonObject
        {
            ["provider"] = "widnai",
            ["type"] = "translation-quality",
            ["operation"] = operation,
            ["model"] = model,
            ["input"] = new JsonObject
            {
                ["sourceText"] = sourceText,
                ["targetText"] = targetText,
                ["referenceText"] = referenceText,
                ["tone"] = tone,
                ["targetLocale"] = targetLocale
            },
            ["segments"] = normalizedSegments,
            ["raw"] = raw.DeepClone()
        };
    }

    private static JsonArray NormalizeErrorSpans(JsonArray? spans)
    {
        var normalized = new JsonArray();
        if (spans == null) return normalized;

        foreach (var item in spans)
        {
            if (item is not JsonObject spanObj)
                continue;

            normalized.Add(new JsonObject
            {
                ["text"] = spanObj["text"]?.GetValue<string>(),
                ["confidence"] = spanObj["confidence"]?.GetValue<double?>(),
                ["severity"] = spanObj["severity"]?.GetValue<string>(),
                ["start"] = spanObj["start"]?.GetValue<int?>(),
                ["end"] = spanObj["end"]?.GetValue<int?>()
            });
        }

        return normalized;
    }

    private static string NormalizeEvaluateModel(string? model)
    {
        var m = (model ?? "xcomet-xl").Trim().ToLowerInvariant();
        return m == "xcomet-xl" ? "xcomet-xl" : "xcomet-xl";
    }

    private static string NormalizeEstimateModel(string? model)
    {
        var m = (model ?? "mqm-qe").Trim().ToLowerInvariant();
        return m is "mqm-qe" or "xcomet-xl" ? m : "mqm-qe";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
