using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using MCPhappey.Tools.StabilityAI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SmallestAI;

public static class SmallestAITranscriptions
{
    private const string PulsePath = "api/v1/pulse/get_text";

    [Description("Transcribe audio from fileUrl with Smallest AI Pulse STT and return structured transcription output.")]
    [McpServerTool(
        Title = "Smallest AI Transcriptions",
        Name = "smallestai_transcriptions_transcribe",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SmallestAI_Transcriptions_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe (.wav, .mp3, .m4a, .webm, etc). Supports SharePoint/OneDrive/HTTP.")] string fileUrl,
        [Description("Model. Default: pulse.")] string model = "pulse",
        [Description("Language code (ISO 639-1) or multi for autodetect. Default: en.")] string language = "en",
        [Description("Optional webhook URL.")] string? webhook_url = null,
        [Description("Optional webhook extra key:value pairs, comma-separated.")] string? webhook_extra = null,
        [Description("Include word/utterance timestamps. Default: false.")] bool word_timestamps = false,
        [Description("Enable speaker diarization. Default: false.")] bool diarize = false,
        [Description("Enable age detection. Default: false.")] bool age_detection = false,
        [Description("Enable gender detection. Default: false.")] bool gender_detection = false,
        [Description("Enable emotion detection. Default: false.")] bool emotion_detection = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SmallestAITranscriptionRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    Language = NormalizeLanguage(language),
                    WebhookUrl = NormalizeOptional(webhook_url),
                    WebhookExtra = NormalizeOptional(webhook_extra),
                    WordTimestamps = word_timestamps,
                    Diarize = diarize,
                    AgeDetection = age_detection,
                    GenderDetection = gender_detection,
                    EmotionDetection = emotion_detection,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var downloads = await downloadService.DownloadContentAsync(
                serviceProvider,
                requestContext.Server,
                typed.FileUrl,
                cancellationToken);

            var mediaFile = downloads.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

            using var client = serviceProvider.CreateSmallestAIClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, BuildPulseUrl(typed));
            req.Content = new StreamContent(mediaFile.Contents.ToStream());
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(mediaFile.MimeType)
                    ? "application/octet-stream"
                    : mediaFile.MimeType);

            using var resp = await client.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Smallest AI Pulse STT failed ({(int)resp.StatusCode}): {body}");

            var transcript = ExtractTranscript(body);
            var safeName = typed.Filename.ToOutputFileName();
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.txt",
                BinaryData.FromString(transcript),
                cancellationToken);

            var structured = BuildStructuredResponse(typed, body, transcript, uploaded);
            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    transcript.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    private static string BuildPulseUrl(SmallestAITranscriptionRequest typed)
    {
        var query = new List<string>
        {
            $"model={Uri.EscapeDataString(typed.Model)}",
            $"language={Uri.EscapeDataString(typed.Language)}",
            $"word_timestamps={typed.WordTimestamps.ToString().ToLowerInvariant()}",
            $"diarize={typed.Diarize.ToString().ToLowerInvariant()}",
            $"age_detection={(typed.AgeDetection ? "true" : "false")}",
            $"gender_detection={(typed.GenderDetection ? "true" : "false")}",
            $"emotion_detection={(typed.EmotionDetection ? "true" : "false")}" 
        };

        if (!string.IsNullOrWhiteSpace(typed.WebhookUrl))
            query.Add($"webhook_url={Uri.EscapeDataString(typed.WebhookUrl)}");

        if (!string.IsNullOrWhiteSpace(typed.WebhookExtra))
            query.Add($"webhook_extra={Uri.EscapeDataString(typed.WebhookExtra)}");

        return $"{PulsePath}?{string.Join("&", query)}";
    }

    private static string ExtractTranscript(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("transcription", out var transcription)
                && transcription.ValueKind == JsonValueKind.String)
                return transcription.GetString() ?? string.Empty;

            return json;
        }
        catch
        {
            return json;
        }
    }

    private static JsonObject BuildStructuredResponse(
        SmallestAITranscriptionRequest typed,
        string rawBody,
        string transcript,
        ResourceLinkBlock? uploaded)
    {
        JsonNode raw;
        try
        {
            raw = JsonNode.Parse(rawBody) ?? JsonValue.Create(rawBody)!;
        }
        catch
        {
            raw = JsonValue.Create(rawBody)!;
        }

        return new JsonObject
        {
            ["provider"] = "smallestai",
            ["type"] = "transcription",
            ["model"] = typed.Model,
            ["fileUrl"] = typed.FileUrl,
            ["language"] = typed.Language,
            ["word_timestamps"] = typed.WordTimestamps,
            ["diarize"] = typed.Diarize,
            ["age_detection"] = typed.AgeDetection,
            ["gender_detection"] = typed.GenderDetection,
            ["emotion_detection"] = typed.EmotionDetection,
            ["text"] = transcript,
            ["output"] = new JsonObject
            {
                ["transcriptFileUri"] = uploaded?.Uri,
                ["transcriptFileName"] = uploaded?.Name,
                ["transcriptFileMimeType"] = uploaded?.MimeType
            },
            ["raw"] = raw
        };
    }

    private static string NormalizeModel(string? value)
    {
        var model = (value ?? "pulse").Trim().ToLowerInvariant();
        return model == "pulse" ? model : "pulse";
    }

    private static string NormalizeLanguage(string? value)
    {
        var language = (value ?? "en").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(language) ? "en" : language;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [Description("Please fill in the Smallest AI transcription request.")]
    public sealed class SmallestAITranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("STT model. Default: pulse.")]
        public string Model { get; set; } = "pulse";

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code or multi.")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("webhook_url")]
        [Description("Optional webhook URL.")]
        public string? WebhookUrl { get; set; }

        [JsonPropertyName("webhook_extra")]
        [Description("Optional webhook extra key:value pairs.")]
        public string? WebhookExtra { get; set; }

        [JsonPropertyName("word_timestamps")]
        [Description("Include word timestamps.")]
        public bool WordTimestamps { get; set; }

        [JsonPropertyName("diarize")]
        [Description("Enable diarization.")]
        public bool Diarize { get; set; }

        [JsonPropertyName("age_detection")]
        [Description("Enable age detection.")]
        public bool AgeDetection { get; set; }

        [JsonPropertyName("gender_detection")]
        [Description("Enable gender detection.")]
        public bool GenderDetection { get; set; }

        [JsonPropertyName("emotion_detection")]
        [Description("Enable emotion detection.")]
        public bool EmotionDetection { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

