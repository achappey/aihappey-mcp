using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.YourVoic;

public static class YourVoicTranscriptions
{
    private const string SttTranscribePath = "stt/transcribe";

    [Description("Transcribe audio from fileUrl using YourVoic and return structured content.")]
    [McpServerTool(
        Title = "YourVoic Speech-to-Text",
        Name = "yourvoic_transcriptions_transcribe",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> YourVoic_Transcriptions_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe (.mp3, .wav, .m4a, .webm). Supports SharePoint/OneDrive/HTTP.")]
        string fileUrl,
        [Description("Model id. Default: cipher-fast. Options include cipher-fast, cipher-max, lucid-mono, lucid-multi, lucid-agent, lucid-lite.")]
        string model = "cipher-fast",
        [Description("Optional language code (e.g. en, nl, es).")]
        string? language = null,
        [Description("Response format: json, text, verbose_json, srt, vtt. Default: json.")]
        string responseFormat = "json",
        [Description("Optional prompt for cipher models.")]
        string? prompt = null,
        [Description("Timestamp granularity for cipher verbose_json: word or segment.")]
        string? timestampGranularities = null,
        [Description("Enable speaker diarization for lucid models. Default: false.")]
        bool diarize = false,
        [Description("Enable smart formatting for lucid models. Default: true.")]
        bool smartFormat = true,
        [Description("Enable punctuation for lucid models. Default: true.")]
        bool punctuate = true,
        [Description("Comma-separated keywords boost for lucid models.")]
        string? keywords = null,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new YourVoicTranscriptionRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    Language = language,
                    ResponseFormat = NormalizeResponseFormat(responseFormat),
                    Prompt = prompt,
                    TimestampGranularities = NormalizeTimestampGranularity(timestampGranularities),
                    Diarize = diarize,
                    SmartFormat = smartFormat,
                    Punctuate = punctuate,
                    Keywords = keywords,
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

            var media = downloads.FirstOrDefault()
                ?? throw new InvalidOperationException("Failed to download media from fileUrl.");

            using var form = new MultipartFormDataContent
            {
                {
                    new StreamContent(media.Contents.ToStream())
                    {
                        Headers =
                        {
                            ContentType = new MediaTypeHeaderValue(
                                string.IsNullOrWhiteSpace(media.MimeType)
                                    ? "application/octet-stream"
                                    : media.MimeType)
                        }
                    },
                    "file",
                    string.IsNullOrWhiteSpace(media.Filename) ? "input.bin" : media.Filename
                },
                "model".NamedField(typed.Model),
                "response_format".NamedField(NormalizeResponseFormat(typed.ResponseFormat))
            };

            if (!string.IsNullOrWhiteSpace(typed.Language))
                form.Add("language".NamedField(typed.Language));

            if (IsLucidModel(typed.Model))
            {
                form.Add("diarize".NamedField(typed.Diarize.ToString().ToLowerInvariant()));
                form.Add("smart_format".NamedField(typed.SmartFormat.ToString().ToLowerInvariant()));
                form.Add("punctuate".NamedField(typed.Punctuate.ToString().ToLowerInvariant()));

                if (!string.IsNullOrWhiteSpace(typed.Keywords))
                    form.Add("keywords".NamedField(typed.Keywords));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(typed.Prompt))
                    form.Add("prompt".NamedField(typed.Prompt));

                if (string.Equals(typed.ResponseFormat, "verbose_json", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(typed.TimestampGranularities))
                {
                    form.Add("timestamp_granularities".NamedField(typed.TimestampGranularities));
                }
            }

            using var client = serviceProvider.CreateYourVoicClient();
            using var resp = await client.PostAsync(SttTranscribePath, form, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"YourVoic STT failed ({(int)resp.StatusCode}): {body}");

            var text = ExtractTranscriptionText(body, typed.ResponseFormat);
            var safeName = typed.Filename.ToOutputFileName();
            var uploadExt = NormalizeResponseFormat(typed.ResponseFormat) switch
            {
                "srt" => "srt",
                "vtt" => "vtt",
                "verbose_json" => "json",
                "json" => "json",
                _ => "txt"
            };

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.{uploadExt}",
                BinaryData.FromString(body),
                cancellationToken);

            var structured = BuildStructuredResponse(typed, body, text, uploaded);

            return new CallToolResult
            {
                StructuredContent = structured,
                Content =
                [
                    text.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    [Description("Please fill in the YourVoic transcription request.")]
    public sealed class YourVoicTranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model id. Default: cipher-fast.")]
        public string Model { get; set; } = "cipher-fast";

        [JsonPropertyName("language")]
        [Description("Optional language code.")]
        public string? Language { get; set; }

        [JsonPropertyName("response_format")]
        [Required]
        [Description("json, text, verbose_json, srt, or vtt.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("prompt")]
        [Description("Optional prompt for cipher models.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("timestamp_granularities")]
        [Description("word or segment (for cipher verbose_json).")]
        public string? TimestampGranularities { get; set; }

        [JsonPropertyName("diarize")]
        [Description("Enable diarization for lucid models.")]
        public bool Diarize { get; set; }

        [JsonPropertyName("smart_format")]
        [Description("Enable smart formatting for lucid models.")]
        public bool SmartFormat { get; set; } = true;

        [JsonPropertyName("punctuate")]
        [Description("Enable punctuation for lucid models.")]
        public bool Punctuate { get; set; } = true;

        [JsonPropertyName("keywords")]
        [Description("Comma-separated keywords for lucid models.")]
        public string? Keywords { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "cipher-fast").Trim().ToLowerInvariant();
        return value is "cipher-fast" or "cipher-max" or "lucid-mono" or "lucid-multi" or "lucid-agent" or "lucid-lite"
            ? value
            : "cipher-fast";
    }

    private static bool IsLucidModel(string? model)
        => !string.IsNullOrWhiteSpace(model)
           && model.StartsWith("lucid-", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeResponseFormat(string? responseFormat)
    {
        var value = (responseFormat ?? "json").Trim().ToLowerInvariant();
        return value is "json" or "text" or "verbose_json" or "srt" or "vtt" ? value : "json";
    }

    private static string? NormalizeTimestampGranularity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "word" or "segment" ? normalized : null;
    }

    private static string ExtractTranscriptionText(string responseBody, string responseFormat)
    {
        var format = NormalizeResponseFormat(responseFormat);
        if (format is "text" or "srt" or "vtt")
            return responseBody;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString() ?? string.Empty;
            }

            return responseBody;
        }
        catch
        {
            return responseBody;
        }
    }

    private static JsonObject BuildStructuredResponse(
        YourVoicTranscriptionRequest typed,
        string rawBody,
        string text,
        ResourceLinkBlock? uploaded)
    {
        JsonNode rawNode;
        try
        {
            rawNode = JsonNode.Parse(rawBody) ?? JsonValue.Create(rawBody)!;
        }
        catch
        {
            rawNode = JsonValue.Create(rawBody)!;
        }

        return new JsonObject
        {
            ["provider"] = "yourvoic",
            ["type"] = "transcription",
            ["model"] = typed.Model,
            ["fileUrl"] = typed.FileUrl,
            ["language"] = typed.Language,
            ["response_format"] = NormalizeResponseFormat(typed.ResponseFormat),
            ["text"] = text,
            ["output"] = new JsonObject
            {
                ["transcriptFileUri"] = uploaded?.Uri,
                ["transcriptFileName"] = uploaded?.Name,
                ["transcriptFileMimeType"] = uploaded?.MimeType
            },
            ["raw"] = rawNode
        };
    }
}

