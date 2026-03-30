using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Venice;

public static class VeniceAudio
{
    private const string SpeechPath = "audio/speech";
    private const string TranscriptionsPath = "audio/transcriptions";

    [Description("Generate speech audio from text using Venice AI and upload the result as a resource link block.")]
    [McpServerTool(
        Title = "Venice Text-to-Speech",
        Name = "venice_audio_speech_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Audio_Speech_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to generate audio for (1..4096 chars).")]
        string input,
        [Description("TTS model. Default: tts-kokoro.")]
        string model = "tts-kokoro",
        [Description("Audio response format: mp3, opus, aac, flac, wav, pcm. Default: mp3.")]
        string response_format = "mp3",
        [Description("Speech speed from 0.25 to 4.0. Default: 1.0.")]
        double speed = 1.0,
        [Description("Stream sentence-by-sentence when true. Default: false.")]
        bool streaming = false,
        [Description("Voice ID. Default: af_sky.")]
        string voice = "af_sky",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceSpeechRequest
                {
                    Input = input,
                    Model = NormalizeSpeechModel(model),
                    ResponseFormat = NormalizeSpeechResponseFormat(response_format),
                    Speed = Math.Clamp(speed, 0.25, 4.0),
                    Streaming = streaming,
                    Voice = NormalizeSpeechVoice(voice),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            if (string.IsNullOrWhiteSpace(typed.Input))
                throw new ValidationException("input is required.");

            if (typed.Input.Length > 4096)
                throw new ValidationException("input exceeds maximum length of 4096 characters.");

            using var client = serviceProvider.CreateVeniceClient(GetSpeechAcceptMimeType(typed.ResponseFormat));
            using var req = new HttpRequestMessage(HttpMethod.Post, SpeechPath)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    input = typed.Input,
                    model = typed.Model,
                    response_format = typed.ResponseFormat,
                    speed = typed.Speed,
                    streaming = typed.Streaming,
                    voice = typed.Voice
                }), Encoding.UTF8, MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var body = Encoding.UTF8.GetString(bytes);
                throw new InvalidOperationException($"Venice speech request failed ({(int)resp.StatusCode}): {body}");
            }

            if (bytes.Length == 0)
                throw new InvalidOperationException("Venice speech request returned empty audio data.");

            var ext = NormalizeSpeechResponseFormat(typed.ResponseFormat);
            var uploadName = typed.Filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
                ? typed.Filename
                : $"{typed.Filename}.{ext}";

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Transcribe audio from fileUrl using Venice AI and return structured content.")]
    [McpServerTool(
        Title = "Venice Audio Transcriptions",
        Name = "venice_audio_transcriptions_create",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Audio_Transcriptions_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe (SharePoint, OneDrive, or HTTP).")]
        string fileUrl,
        [Description("Transcription model. Default: nvidia/parakeet-tdt-0.6b-v3.")]
        string model = "nvidia/parakeet-tdt-0.6b-v3",
        [Description("Output format: json or text. Default: json.")]
        string response_format = "json",
        [Description("Include timestamps in the response. Default: false.")]
        bool timestamps = false,
        [Description("Optional ISO 639-1 language code, e.g. en, es, fr.")]
        string? language = null,
        [Description("Output filename without extension for optional transcript upload.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceTranscriptionRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeTranscriptionModel(model),
                    ResponseFormat = NormalizeTranscriptionResponseFormat(response_format),
                    Timestamps = timestamps,
                    Language = NormalizeOptional(language),
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

            using var form = new MultipartFormDataContent();
            var fileName = string.IsNullOrWhiteSpace(mediaFile.Filename) ? "audio.bin" : mediaFile.Filename;
            var fileContent = new StreamContent(mediaFile.Contents.ToStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(mediaFile.MimeType)
                    ? "application/octet-stream"
                    : mediaFile.MimeType);
            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent(typed.Model), "model");
            form.Add(new StringContent(typed.ResponseFormat), "response_format");
            form.Add(new StringContent(typed.Timestamps ? "true" : "false"), "timestamps");

            if (!string.IsNullOrWhiteSpace(typed.Language))
                form.Add(new StringContent(typed.Language), "language");

            using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, TranscriptionsPath)
            {
                Content = form
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Venice transcription request failed ({(int)resp.StatusCode}): {raw}");

            var text = ExtractTranscriptionText(raw);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(text),
                cancellationToken);

            var structured = BuildTranscriptionStructured(typed, raw, text, uploaded);

            return new CallToolResult
            {
                StructuredContent = (structured).ToJsonElement(),
                Content =
                [
                    text.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    private static JsonObject BuildTranscriptionStructured(
        VeniceTranscriptionRequest typed,
        string raw,
        string text,
        ResourceLinkBlock? uploaded)
    {
        JsonNode parsed;
        try
        {
            parsed = JsonNode.Parse(raw) ?? JsonValue.Create(raw)!;
        }
        catch
        {
            parsed = JsonValue.Create(raw)!;
        }

        return new JsonObject
        {
            ["provider"] = "venice",
            ["type"] = "transcription",
            ["fileUrl"] = typed.FileUrl,
            ["model"] = typed.Model,
            ["response_format"] = typed.ResponseFormat,
            ["timestamps"] = typed.Timestamps,
            ["language"] = typed.Language,
            ["text"] = text,
            ["output"] = new JsonObject
            {
                ["transcriptFileUri"] = uploaded?.Uri,
                ["transcriptFileName"] = uploaded?.Name,
                ["transcriptFileMimeType"] = uploaded?.MimeType
            },
            ["raw"] = parsed
        };
    }

    private static string ExtractTranscriptionText(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                return textEl.GetString() ?? string.Empty;
        }
        catch
        {
            // response_format=text may not be JSON
        }

        return raw;
    }

    private static string NormalizeSpeechModel(string? value)
    {
        var model = (value ?? "tts-kokoro").Trim();
        return string.Equals(model, "tts-kokoro", StringComparison.OrdinalIgnoreCase)
            ? "tts-kokoro"
            : "tts-kokoro";
    }

    private static string NormalizeSpeechResponseFormat(string? value)
    {
        var format = (value ?? "mp3").Trim().ToLowerInvariant();
        return format is "mp3" or "opus" or "aac" or "flac" or "wav" or "pcm" ? format : "mp3";
    }

    private static string GetSpeechAcceptMimeType(string responseFormat)
    {
        var format = NormalizeSpeechResponseFormat(responseFormat);
        return format switch
        {
            "mp3" => "audio/mpeg",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "pcm" => "audio/pcm",
            _ => "audio/mpeg"
        };
    }

    private static string NormalizeSpeechVoice(string? value)
    {
        var voice = (value ?? "af_sky").Trim();
        return string.IsNullOrWhiteSpace(voice) ? "af_sky" : voice;
    }

    private static string NormalizeTranscriptionModel(string? value)
    {
        var model = (value ?? "nvidia/parakeet-tdt-0.6b-v3").Trim();
        return model is "nvidia/parakeet-tdt-0.6b-v3" or "openai/whisper-large-v3"
            ? model
            : "nvidia/parakeet-tdt-0.6b-v3";
    }

    private static string NormalizeTranscriptionResponseFormat(string? value)
    {
        var format = (value ?? "json").Trim().ToLowerInvariant();
        return format is "json" or "text" ? format : "json";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [Description("Please fill in the Venice text-to-speech request.")]
    public sealed class VeniceSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Text to generate audio for.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("TTS model ID. Default: tts-kokoro.")]
        public string Model { get; set; } = "tts-kokoro";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output audio format.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        [Range(0.25, 4.0)]
        [Description("Speech speed from 0.25 to 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("streaming")]
        [Description("Whether to stream response from Venice.")]
        public bool Streaming { get; set; } = false;

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice ID.")]
        public string Voice { get; set; } = "af_sky";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Venice transcription request.")]
    public sealed class VeniceTranscriptionRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Transcription model ID.")]
        public string Model { get; set; } = "nvidia/parakeet-tdt-0.6b-v3";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Transcript response format: json or text.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("timestamps")]
        [Description("Include timestamps in output when true.")]
        public bool Timestamps { get; set; } = false;

        [JsonPropertyName("language")]
        [Description("Optional ISO 639-1 language code.")]
        public string? Language { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output transcript filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

