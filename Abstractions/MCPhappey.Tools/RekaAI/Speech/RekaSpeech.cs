using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.RekaAI.Speech;

public static class RekaSpeech
{
    private const string TranscriptionOrTranslationUrl = "https://api.reka.ai/v1/transcription_or_translation";

    [Description("Transcribe speech from an audio file URL using Reka Speech.")]
    [McpServerTool(
        Title = "Reka Speech Transcribe",
        Name = "reka_speech_transcribe",
        Destructive = false)]
    public static async Task<CallToolResult?> RekaSpeech_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Sampling rate in Hz. Default: 16000.")]
        int samplingRate = 16000,
        [Description("Optional target language. Supported: french, spanish, japanese, chinese, korean, italian, portuguese, german.")]
        string? targetLanguage = null,
        [Description("Optional generation temperature. Use 0.0 for deterministic output.")]
        double? temperature = null,
        [Description("Optional max token limit.")]
        int? maxTokens = null,
        [Description("Optional filename stem for uploaded artifacts.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                _ = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new RekaTranscribeInput
                    {
                        FileUrl = fileUrl,
                        SamplingRate = samplingRate,
                        TargetLanguage = targetLanguage,
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "No input data provided".ToErrorCallToolResponse();

                var payload = BuildPayload(
                    typed.FileUrl,
                    typed.SamplingRate,
                    typed.TargetLanguage,
                    typed.TargetLanguage is not null,
                    returnTranslationAudio: false,
                    typed.Temperature,
                    typed.MaxTokens);

                using var client = clientFactory.CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, TranscriptionOrTranslationUrl)
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);

                using var resp = await client.SendAsync(req, cancellationToken);
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"{resp.StatusCode}: {json}");

                var responseNode = JsonNode.Parse(json)?.AsObject() ?? [];

                var transcript = responseNode["transcript"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(transcript))
                {
                    var uploadedTxt = await requestContext.Server.Upload(
                        serviceProvider,
                        $"{typed.Filename}.txt",
                        BinaryData.FromString(transcript),
                        cancellationToken);

                    return new CallToolResult
                    {
                        StructuredContent = responseNode,
                        Content = uploadedTxt != null
                            ? [transcript.ToTextContentBlock(), uploadedTxt]
                            : [transcript.ToTextContentBlock()]
                    };
                }

                return new CallToolResult
                {
                    StructuredContent = responseNode,
                    Content = [json.ToTextContentBlock()]
                };
            }));

    [Description("Translate speech from an audio file URL and generate translated speech audio using Reka Speech.")]
    [McpServerTool(
        Title = "Reka Speech Translate Speech",
        Name = "reka_speech_translate_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> RekaSpeech_TranslateSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Target language. Supported: french, spanish, japanese, chinese, korean, italian, portuguese, german.")]
        string targetLanguage,
        [Description("Sampling rate in Hz. Default: 16000.")]
        int samplingRate = 16000,
        [Description("Optional generation temperature. Use 0.0 for deterministic output.")]
        double? temperature = null,
        [Description("Optional max token limit.")]
        int? maxTokens = null,
        [Description("Optional filename stem for uploaded artifact.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");
                if (string.IsNullOrWhiteSpace(targetLanguage))
                    throw new ArgumentException("targetLanguage is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                _ = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new RekaTranslateSpeechInput
                    {
                        FileUrl = fileUrl,
                        TargetLanguage = targetLanguage,
                        SamplingRate = samplingRate,
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "No input data provided".ToErrorCallToolResponse();

                var payload = BuildPayload(
                    typed.FileUrl,
                    typed.SamplingRate,
                    typed.TargetLanguage,
                    isTranslate: true,
                    returnTranslationAudio: true,
                    typed.Temperature,
                    typed.MaxTokens);

                using var client = clientFactory.CreateClient();
                using var req = new HttpRequestMessage(HttpMethod.Post, TranscriptionOrTranslationUrl)
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);

                using var resp = await client.SendAsync(req, cancellationToken);
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"{resp.StatusCode}: {json}");

                var responseNode = JsonNode.Parse(json)?.AsObject() ?? [];
                var transcript = responseNode["transcript"]?.GetValue<string>();
                var translation = responseNode["translation"]?.GetValue<string>();
                var audioBase64 = responseNode["audio_base64"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(audioBase64))
                    throw new Exception("Reka did not return audio_base64 for speech translation.");

                var audioBytes = Convert.FromBase64String(audioBase64);
                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{typed.Filename}.wav",
                    BinaryData.FromBytes(audioBytes),
                    cancellationToken);

                if (uploaded == null)
                    throw new Exception("Audio upload failed.");

                responseNode["uploaded_audio_resource_url"] = uploaded.Uri;

                var content = new List<ContentBlock>
                {
                    uploaded
                };

                if (!string.IsNullOrWhiteSpace(transcript))
                    content.Add(transcript.ToTextContentBlock());

                if (!string.IsNullOrWhiteSpace(translation))
                    content.Add($"Translation: {translation}".ToTextContentBlock());

                return new CallToolResult
                {
                    StructuredContent = responseNode,
                    Content = [.. content]
                };
            }));

    private static JsonObject BuildPayload(
        string audioUrl,
        int samplingRate,
        string? targetLanguage,
        bool isTranslate,
        bool returnTranslationAudio,
        double? temperature,
        int? maxTokens)
    {
        var payload = new JsonObject
        {
            ["audio_url"] = audioUrl,
            ["sampling_rate"] = samplingRate,
            ["is_translate"] = isTranslate,
            ["return_translation_audio"] = returnTranslationAudio
        };

        if (!string.IsNullOrWhiteSpace(targetLanguage))
            payload["target_language"] = targetLanguage;

        if (temperature.HasValue)
            payload["temperature"] = temperature.Value;

        if (maxTokens.HasValue)
            payload["max_tokens"] = maxTokens.Value;

        return payload;
    }

    [Description("Please fill in Reka speech transcription request details.")]
    public class RekaTranscribeInput
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL (SharePoint/OneDrive/HTTPS).")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("samplingRate")]
        [Range(8000, 192000)]
        [Description("Sampling rate in Hz.")]
        public int SamplingRate { get; set; } = 16000;

        [JsonPropertyName("targetLanguage")]
        [Description("Optional target language for translated text output.")]
        public string? TargetLanguage { get; set; }

        [JsonPropertyName("temperature")]
        [Description("Optional generation temperature.")]
        public double? Temperature { get; set; }

        [JsonPropertyName("maxTokens")]
        [Range(1, int.MaxValue)]
        [Description("Optional max token limit.")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename stem.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in Reka speech translation request details.")]
    public class RekaTranslateSpeechInput
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL (SharePoint/OneDrive/HTTPS).")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("targetLanguage")]
        [Required]
        [Description("Target language.")]
        public string TargetLanguage { get; set; } = default!;

        [JsonPropertyName("samplingRate")]
        [Range(8000, 192000)]
        [Description("Sampling rate in Hz.")]
        public int SamplingRate { get; set; } = 16000;

        [JsonPropertyName("temperature")]
        [Description("Optional generation temperature.")]
        public double? Temperature { get; set; }

        [JsonPropertyName("maxTokens")]
        [Range(1, int.MaxValue)]
        [Description("Optional max token limit.")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename stem.")]
        public string Filename { get; set; } = default!;
    }
}

