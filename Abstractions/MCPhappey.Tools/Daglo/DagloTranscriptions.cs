using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Daglo;

public static class DagloTranscriptions
{
    private const string CreatePath = "stt/v1/async/transcripts";

    [Description("Create a Daglo async transcription from fileUrl and return structured transcription content.")]
    [McpServerTool(
        Title = "Daglo Audio Transcription",
        Name = "daglo_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Daglo_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to transcribe (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Transcription model. Default: general.")]
        string model = "general",
        [Description("Language code (ko-KR, en-US, mixed, ja-JP, etc.). Default: ko-KR.")]
        string language = "ko-KR",
        [Description("Enable speaker diarization.")]
        bool speakerDiarizationEnable = false,
        [Description("Optional speaker count hint when diarization is enabled (>=2).")]
        int? speakerCountHint = null,
        [Description("Enable keyword extraction.")]
        bool keywordExtractionEnable = false,
        [Description("Maximum extracted keywords when enabled.")]
        int? keywordExtractionMaxCount = null,
        [Description("Enable sentiment analysis.")]
        bool sentimentAnalysisEnable = false,
        [Description("Polling interval in seconds. Default: 2.")]
        int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds. Default: 1800.")]
        int maxWaitSeconds = 1800,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new DagloTranscriptionRequest
                    {
                        FileUrl = fileUrl,
                        Model = NormalizeModel(model),
                        Language = NormalizeLanguage(language),
                        SpeakerDiarizationEnable = speakerDiarizationEnable,
                        SpeakerCountHint = speakerCountHint,
                        KeywordExtractionEnable = keywordExtractionEnable,
                        KeywordExtractionMaxCount = keywordExtractionMaxCount,
                        SentimentAnalysisEnable = sentimentAnalysisEnable,
                        PollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds),
                        MaxWaitSeconds = Math.Max(30, maxWaitSeconds)
                    },
                    cancellationToken);

                if (notAccepted != null)
                    throw new ValidationException("Transcription request was not accepted.");

                if (typed == null)
                    throw new ValidationException("No transcription input data provided.");

                ValidateRequest(typed);

                var daglo = serviceProvider.GetRequiredService<DagloClient>();

                var createBody = BuildCreatePayload(typed);
                using var created = await daglo.PostJsonAsync(CreatePath, createBody, cancellationToken);
                var rid = GetString(created.RootElement, "rid");

                if (string.IsNullOrWhiteSpace(rid))
                    throw new InvalidOperationException("Daglo did not return rid for async transcription request.");

                var getPath = $"stt/v1/async/transcripts/{Uri.EscapeDataString(rid)}";
                var sw = Stopwatch.StartNew();

                string status = "ai_requested";
                JsonObject lastPayload = new();

                while (true)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(typed.MaxWaitSeconds))
                        throw new TimeoutException($"Daglo transcription timed out after {typed.MaxWaitSeconds} seconds.");

                    cancellationToken.ThrowIfCancellationRequested();

                    using var polled = await daglo.GetJsonOrNullAsync(getPath, cancellationToken);
                    if (polled == null)
                    {
                        status = "no_content";
                        break;
                    }

                    status = GetString(polled.RootElement, "status") ?? "unknown";
                    lastPayload = JsonNode.Parse(polled.RootElement.GetRawText())?.AsObject() ?? new JsonObject();

                    if (IsCompleted(status) || IsFailed(status))
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(typed.PollingIntervalSeconds), cancellationToken);
                }

                return new JsonObject
                {
                    ["provider"] = "daglo",
                    ["rid"] = rid,
                    ["status"] = status,
                    ["isCompleted"] = IsCompleted(status),
                    ["isFailed"] = IsFailed(status),
                    ["result"] = lastPayload
                };
            }));

    private static object BuildCreatePayload(DagloTranscriptionRequest request)
    {
        var sttConfig = new JsonObject
        {
            ["model"] = request.Model,
            ["language"] = request.Language
        };

        if (request.SpeakerDiarizationEnable)
        {
            var speakerDiarization = new JsonObject
            {
                ["enable"] = true
            };

            if (request.SpeakerCountHint.HasValue)
                speakerDiarization["speakerCountHint"] = request.SpeakerCountHint.Value;

            sttConfig["speakerDiarization"] = speakerDiarization;
        }

        var nlpConfig = new JsonObject();

        if (request.KeywordExtractionEnable)
        {
            var keywordExtraction = new JsonObject
            {
                ["enable"] = true
            };

            if (request.KeywordExtractionMaxCount.HasValue)
                keywordExtraction["maxCount"] = request.KeywordExtractionMaxCount.Value;

            nlpConfig["keywordExtraction"] = keywordExtraction;
        }

        if (request.SentimentAnalysisEnable)
        {
            nlpConfig["sentimentAnalysis"] = new JsonObject
            {
                ["enable"] = true
            };
        }

        var payload = new JsonObject
        {
            ["audio"] = new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["url"] = request.FileUrl
                }
            },
            ["sttConfig"] = sttConfig
        };

        if (nlpConfig.Count > 0)
            payload["nlpConfig"] = nlpConfig;

        return payload;
    }

    private static void ValidateRequest(DagloTranscriptionRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileUrl);

        if (request.SpeakerCountHint.HasValue && request.SpeakerCountHint.Value < 2)
            throw new ValidationException("speakerCountHint must be >= 2.");

        if (request.KeywordExtractionMaxCount.HasValue && request.KeywordExtractionMaxCount.Value < 1)
            throw new ValidationException("keywordExtractionMaxCount must be >= 1.");
    }

    private static string NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? "general" : model.Trim();

    private static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? "ko-KR" : language.Trim();

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool IsCompleted(string? status)
        => string.Equals(status, "transcribed", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string? status)
        => string.Equals(status, "input_error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "transcript_error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "file_error", StringComparison.OrdinalIgnoreCase);
}

[Description("Please fill in the Daglo transcription request.")]
public sealed class DagloTranscriptionRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Audio/video file URL to transcribe.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Transcription model. Default: general.")]
    public string Model { get; set; } = "general";

    [JsonPropertyName("language")]
    [Required]
    [Description("Language code for transcription. Default: ko-KR.")]
    public string Language { get; set; } = "ko-KR";

    [JsonPropertyName("speaker_diarization_enable")]
    [Description("Enable speaker diarization.")]
    public bool SpeakerDiarizationEnable { get; set; }

    [JsonPropertyName("speaker_count_hint")]
    [Description("Optional speaker count hint (>=2).")]
    public int? SpeakerCountHint { get; set; }

    [JsonPropertyName("keyword_extraction_enable")]
    [Description("Enable keyword extraction.")]
    public bool KeywordExtractionEnable { get; set; }

    [JsonPropertyName("keyword_extraction_max_count")]
    [Description("Maximum extracted keywords (>=1).")]
    public int? KeywordExtractionMaxCount { get; set; }

    [JsonPropertyName("sentiment_analysis_enable")]
    [Description("Enable sentiment analysis.")]
    public bool SentimentAnalysisEnable { get; set; }

    [JsonPropertyName("polling_interval_seconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollingIntervalSeconds { get; set; } = 2;

    [JsonPropertyName("max_wait_seconds")]
    [Range(30, 7200)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = 1800;
}

