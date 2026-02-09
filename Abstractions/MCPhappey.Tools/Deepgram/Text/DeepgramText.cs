using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Deepgram;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Deepgram.Text;

public static class DeepgramText
{
    private const string ReadUrl = "https://api.deepgram.com/v1/read";

    [Description("Analyze text content using Deepgram text intelligence API.")]
    [McpServerTool(
        Title = "Deepgram Analyze Text",
        Name = "deepgram_text_analyze_text",
        Destructive = false)]
    public static async Task<CallToolResult?> DeepgramText_AnalyzeText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Plain text to analyze.")] string text,
        [Description("Language hint (BCP-47).")]
        string language = "en",
        [Description("Enable sentiment analysis.")]
        bool sentiment = false,
        [Description("Enable summarization.")]
        bool summarize = false,
        [Description("Enable topic detection.")]
        bool topics = false,
        [Description("Enable intent detection.")]
        bool intents = false,
        [Description("Comma-separated custom topics.")]
        string? customTopic = null,
        [Description("Custom topic mode: extended or strict.")]
        string customTopicMode = "extended",
        [Description("Comma-separated custom intents.")]
        string? customIntent = null,
        [Description("Custom intent mode: extended or strict.")]
        string customIntentMode = "extended",
        [Description("Tag for usage reporting.")]
        string? tag = null,
        [Description("Optional callback URL.")]
        string? callback = null,
        [Description("Callback method: POST or PUT.")]
        string callbackMethod = "POST",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<DeepgramSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post,
                BuildUrlWithQuery(ReadUrl, new Dictionary<string, string?>
                {
                    ["language"] = language,
                    ["sentiment"] = sentiment.ToString().ToLowerInvariant(),
                    ["summarize"] = summarize.ToString().ToLowerInvariant(),
                    ["topics"] = topics.ToString().ToLowerInvariant(),
                    ["intents"] = intents.ToString().ToLowerInvariant(),
                    ["custom_topic"] = customTopic,
                    ["custom_topic_mode"] = customTopicMode,
                    ["custom_intent"] = customIntent,
                    ["custom_intent_mode"] = customIntentMode,
                    ["tag"] = tag,
                    ["callback"] = callback,
                    ["callback_method"] = callbackMethod,
                }));

            req.Headers.TryAddWithoutValidation("Authorization", settings.ApiKey);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            var summaryText = ExtractSummary(json) ?? "Deepgram text analysis completed.";
            var safeName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();

            var uploadedJson = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.json",
                BinaryData.FromString(json),
                cancellationToken);

            var uploadedTxt = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.txt",
                BinaryData.FromString(summaryText),
                cancellationToken);

            return new CallToolResult
            {
                Content =
                [
                    summaryText.ToTextContentBlock(),
                    uploadedTxt!,
                    uploadedJson!,
                ]
            };
        });

    [Description("Download content from fileUrl, convert it to text, and analyze with Deepgram text intelligence.")]
    [McpServerTool(
        Title = "Deepgram Analyze Text From File URL",
        Name = "deepgram_text_analyze_text_from_file_url",
        Destructive = false)]
    public static async Task<CallToolResult?> DeepgramText_AnalyzeTextFromFileUrl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL to a text/PDF/DOCX/HTML/other readable source.")]
        string fileUrl,
        [Description("Language hint (BCP-47).")]
        string language = "en",
        [Description("Enable sentiment analysis.")]
        bool sentiment = false,
        [Description("Enable summarization.")]
        bool summarize = false,
        [Description("Enable topic detection.")]
        bool topics = false,
        [Description("Enable intent detection.")]
        bool intents = false,
        [Description("Comma-separated custom topics.")]
        string? customTopic = null,
        [Description("Custom topic mode: extended or strict.")]
        string customTopicMode = "extended",
        [Description("Comma-separated custom intents.")]
        string? customIntent = null,
        [Description("Custom intent mode: extended or strict.")]
        string customIntentMode = "extended",
        [Description("Tag for usage reporting.")]
        string? tag = null,
        [Description("Optional callback URL.")]
        string? callback = null,
        [Description("Callback method: POST or PUT.")]
        string callbackMethod = "POST",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(sourceText))
                throw new InvalidOperationException("No readable text content found in fileUrl.");

            return await DeepgramText_AnalyzeText(
                serviceProvider,
                requestContext,
                sourceText,
                language,
                sentiment,
                summarize,
                topics,
                intents,
                customTopic,
                customTopicMode,
                customIntent,
                customIntentMode,
                tag,
                callback,
                callbackMethod,
                filename,
                cancellationToken);
        });

    private static string BuildUrlWithQuery(string baseUrl, IDictionary<string, string?> query)
    {
        var parts = query
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}")
            .ToList();

        return parts.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", parts)}";
    }

    private static string? ExtractSummary(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("results", out var results))
            return null;

        // try Read API summary path first
        if (results.TryGetProperty("summary", out var summaryObj)
            && summaryObj.TryGetProperty("results", out var summaryResults)
            && summaryResults.TryGetProperty("summary", out var summary)
            && summary.TryGetProperty("text", out var text))
            return text.GetString();

        return null;
    }
}

