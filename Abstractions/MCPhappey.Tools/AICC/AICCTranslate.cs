using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AICC;

public static class AICCTranslate
{
    private const string TranslatePath = "/v1/aicc/aicc-translator";

    [Description("Translate text list via AI.CC translator endpoint, always confirm via elicitation, and return structured translation content.")]
    [McpServerTool(Title = "AICC Translate Texts", Name = "aicc_translate_texts", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AICC_Translate_Texts(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Original texts as comma/newline-separated values.")] string texts,
        [Description("Target language code (for example: ja, nl, en).")]
        string tl,
        [Description("Optional source language code (for example: en).")]
        string? sl = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new AICCTranslateTextsRequest
                {
                    Texts = texts,
                    Tl = tl,
                    Sl = sl
                },
                cancellationToken);

            var parsedTexts = ParseListInput(typed.Texts);

            ValidateTranslateInput(parsedTexts, typed.Tl);

            var client = serviceProvider.GetRequiredService<AICCClient>();
            var apiResponse = await client.PostJsonAsync(TranslatePath,
                new
                {
                    texts = parsedTexts,
                    tl = typed.Tl,
                    sl = string.IsNullOrWhiteSpace(typed.Sl) ? null : typed.Sl
                },
                cancellationToken);

            var response = new JsonObject
            {
                ["type"] = "aicc_translate_texts",
                ["input"] = new JsonObject
                {
                    ["texts"] = new JsonArray([.. parsedTexts.Select(a => JsonValue.Create(a))]),
                    ["tl"] = typed.Tl,
                    ["sl"] = typed.Sl
                },
                ["result"] = apiResponse
            };

            return response;
        }));

    [Description("Translate text extracted from file URL list via AI.CC translator endpoint, always confirm via elicitation, and return structured translation content.")]
    [McpServerTool(Title = "AICC Translate File URLs", Name = "aicc_translate_fileurls", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AICC_Translate_FileUrls(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URLs as comma/newline-separated values. SharePoint/OneDrive/HTTP supported.")]
        string fileUrls,
        [Description("Target language code (for example: ja, nl, en).")]
        string tl,
        [Description("Optional source language code (for example: en).")]
        string? sl = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new AICCTranslateFileUrlsRequest
                {
                    FileUrls = fileUrls,
                    Tl = tl,
                    Sl = sl
                },
                cancellationToken);

            var urls = ParseListInput(typed.FileUrls);
            ValidateTranslateInput(urls, typed.Tl, "fileUrls");

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var extractedTexts = new List<string>();
            var sourceUrls = new JsonArray();

            foreach (var url in urls)
            {
                var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                var text = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()).Where(t => !string.IsNullOrWhiteSpace(t))).Trim();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    extractedTexts.Add(text);
                    sourceUrls.Add(url);
                }
            }

            if (extractedTexts.Count == 0)
                throw new ValidationException("No readable text content found in provided fileUrls.");

            var client = serviceProvider.GetRequiredService<AICCClient>();
            var apiResponse = await client.PostJsonAsync(TranslatePath,
                new
                {
                    texts = extractedTexts,
                    tl = typed.Tl,
                    sl = string.IsNullOrWhiteSpace(typed.Sl) ? null : typed.Sl
                },
                cancellationToken);

            var response = new JsonObject
            {
                ["type"] = "aicc_translate_fileurls",
                ["input"] = new JsonObject
                {
                    ["fileUrls"] = sourceUrls,
                    ["tl"] = typed.Tl,
                    ["sl"] = typed.Sl
                },
                ["extractedTextsCount"] = extractedTexts.Count,
                ["result"] = apiResponse
            };

            return response;
        }));

    private static List<string> ParseListInput(string value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))];

    private static void ValidateTranslateInput(IReadOnlyCollection<string> values, string tl, string fieldName = "texts")
    {
        if (values.Count == 0)
            throw new ValidationException($"{fieldName} is required and must contain at least one value.");

        if (string.IsNullOrWhiteSpace(tl))
            throw new ValidationException("tl is required.");
    }
}

[Description("Please confirm the AICC translate texts request.")]
public sealed class AICCTranslateTextsRequest
{
    [JsonPropertyName("texts")]
    [Required]
    [Description("Original texts as comma/newline-separated values.")]
    public string Texts { get; set; } = default!;

    [JsonPropertyName("tl")]
    [Required]
    [Description("Target language code.")]
    public string Tl { get; set; } = default!;

    [JsonPropertyName("sl")]
    [Description("Optional source language code.")]
    public string? Sl { get; set; }
}

[Description("Please confirm the AICC translate file URLs request.")]
public sealed class AICCTranslateFileUrlsRequest
{
    [JsonPropertyName("fileUrls")]
    [Required]
    [Description("File URLs as comma/newline-separated values.")]
    public string FileUrls { get; set; } = default!;

    [JsonPropertyName("tl")]
    [Required]
    [Description("Target language code.")]
    public string Tl { get; set; } = default!;

    [JsonPropertyName("sl")]
    [Description("Optional source language code.")]
    public string? Sl { get; set; }
}
