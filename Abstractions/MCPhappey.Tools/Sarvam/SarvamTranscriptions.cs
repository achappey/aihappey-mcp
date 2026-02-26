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
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Sarvam;

public static class SarvamTranscriptions
{
    private const string SpeechToTextUrl = "https://api.sarvam.ai/speech-to-text";
    private const string SpeechToTextTranslateUrl = "https://api.sarvam.ai/speech-to-text-translate";

    [Description("Create speech-to-text transcription from fileUrl using Sarvam and return structured output with uploaded .txt artifact.")]
    [McpServerTool(
        Title = "Sarvam Speech-to-Text",
        Name = "sarvam_transcriptions_speech_to_text_fileurl",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Sarvam_Transcriptions_SpeechToText(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Model: saarika:v2.5 or saaras:v3. Default: saarika:v2.5.")]
        string model = "saarika:v2.5",
        [Description("Mode for saaras:v3: transcribe, translate, verbatim, translit, codemix.")]
        string? mode = null,
        [Description("Language code in BCP-47. Use unknown for autodetect. Default: unknown.")]
        string language_code = "unknown",
        [Description("Optional input audio codec (e.g. wav, mp3, pcm_s16le).")]
        string? input_audio_codec = null,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SarvamSpeechToTextRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeSpeechToTextModel(model),
                    Mode = NormalizeMode(mode),
                    LanguageCode = NormalizeLanguageCode(language_code),
                    InputAudioCodec = NormalizeOptional(input_audio_codec),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await ExecuteSpeechToTextAsync(
                serviceProvider,
                requestContext,
                typed,
                cancellationToken);
        });

    [Description("Create speech-to-text translation from fileUrl using Sarvam and return structured output with uploaded .txt artifact.")]
    [McpServerTool(
        Title = "Sarvam Speech-to-Text Translate",
        Name = "sarvam_transcriptions_speech_to_text_translate_fileurl",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Sarvam_Transcriptions_SpeechToTextTranslate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL to transcribe+translate (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Optional prompt context.")]
        string? prompt = null,
        [Description("Model. Default: saaras:v2.5.")]
        string model = "saaras:v2.5",
        [Description("Optional input audio codec (e.g. wav, mp3, pcm_s16le).")]
        string? input_audio_codec = null,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SarvamSpeechToTextTranslateRequest
                {
                    FileUrl = fileUrl,
                    Prompt = NormalizeOptional(prompt),
                    Model = NormalizeSpeechToTextTranslateModel(model),
                    InputAudioCodec = NormalizeOptional(input_audio_codec),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await ExecuteSpeechToTextTranslateAsync(
                serviceProvider,
                requestContext,
                typed,
                cancellationToken);
        });

    private static async Task<CallToolResult?> ExecuteSpeechToTextAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        SarvamSpeechToTextRequest typed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);

        var settings = serviceProvider.GetRequiredService<SarvamSettings>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var downloads = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            typed.FileUrl,
            cancellationToken);

        var mediaFile = downloads.FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

        using var form = new MultipartFormDataContent
        {
            {
                new StreamContent(mediaFile.Contents.ToStream())
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue(
                            string.IsNullOrWhiteSpace(mediaFile.MimeType)
                                ? "application/octet-stream"
                                : mediaFile.MimeType)
                    }
                },
                "file",
                string.IsNullOrWhiteSpace(mediaFile.Filename) ? "input.bin" : mediaFile.Filename
            },
            "model".NamedField(NormalizeSpeechToTextModel(typed.Model)),
            "language_code".NamedField(NormalizeLanguageCode(typed.LanguageCode))
        };

        if (!string.IsNullOrWhiteSpace(typed.Mode))
            form.Add("mode".NamedField(typed.Mode));

        if (!string.IsNullOrWhiteSpace(typed.InputAudioCodec))
            form.Add("input_audio_codec".NamedField(typed.InputAudioCodec));

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechToTextUrl)
        {
            Content = form
        };
        req.Headers.Add("api-subscription-key", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam speech-to-text call failed ({(int)resp.StatusCode}): {body}");

        var transcript = ExtractTranscript(body);
        var safeName = typed.Filename.ToOutputFileName();
        var uploadedTxt = await requestContext.Server.Upload(
            serviceProvider,
            $"{safeName}.txt",
            BinaryData.FromString(transcript),
            cancellationToken);

        var structured = BuildStructuredResponse("speech_to_text", typed.FileUrl, body, uploadedTxt);

        return new CallToolResult
        {
            StructuredContent = structured,
            Content =
            [
                transcript.ToTextContentBlock(),
                uploadedTxt!
            ]
        };
    }

    private static async Task<CallToolResult?> ExecuteSpeechToTextTranslateAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        SarvamSpeechToTextTranslateRequest typed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typed.FileUrl);

        var settings = serviceProvider.GetRequiredService<SarvamSettings>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var downloads = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            typed.FileUrl,
            cancellationToken);

        var mediaFile = downloads.FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

        using var form = new MultipartFormDataContent
        {
            {
                new StreamContent(mediaFile.Contents.ToStream())
                {
                    Headers =
                    {
                        ContentType = new MediaTypeHeaderValue(
                            string.IsNullOrWhiteSpace(mediaFile.MimeType)
                                ? "application/octet-stream"
                                : mediaFile.MimeType)
                    }
                },
                "file",
                string.IsNullOrWhiteSpace(mediaFile.Filename) ? "input.bin" : mediaFile.Filename
            },
            "model".NamedField(NormalizeSpeechToTextTranslateModel(typed.Model))
        };

        if (!string.IsNullOrWhiteSpace(typed.Prompt))
            form.Add("prompt".NamedField(typed.Prompt));

        if (!string.IsNullOrWhiteSpace(typed.InputAudioCodec))
            form.Add("input_audio_codec".NamedField(typed.InputAudioCodec));

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechToTextTranslateUrl)
        {
            Content = form
        };
        req.Headers.Add("api-subscription-key", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam speech-to-text-translate call failed ({(int)resp.StatusCode}): {body}");

        var transcript = ExtractTranscript(body);
        var safeName = typed.Filename.ToOutputFileName();
        var uploadedTxt = await requestContext.Server.Upload(
            serviceProvider,
            $"{safeName}.txt",
            BinaryData.FromString(transcript),
            cancellationToken);

        var structured = BuildStructuredResponse("speech_to_text_translate", typed.FileUrl, body, uploadedTxt);

        return new CallToolResult
        {
            StructuredContent = structured,
            Content =
            [
                transcript.ToTextContentBlock(),
                uploadedTxt!
            ]
        };
    }

    private static JsonObject BuildStructuredResponse(
        string operation,
        string fileUrl,
        string rawJson,
        ResourceLinkBlock? uploadedTxt)
    {
        var raw = JsonNode.Parse(rawJson) as JsonObject ?? [];
        return new JsonObject
        {
            ["provider"] = "sarvam",
            ["type"] = operation,
            ["fileUrl"] = fileUrl,
            ["request_id"] = raw["request_id"]?.GetValue<string>(),
            ["transcript"] = raw["transcript"]?.GetValue<string>() ?? string.Empty,
            ["language_code"] = raw["language_code"]?.GetValue<string>(),
            ["language_probability"] = raw["language_probability"],
            ["timestamps"] = raw["timestamps"],
            ["diarized_transcript"] = raw["diarized_transcript"],
            ["output"] = new JsonObject
            {
                ["transcriptFileUri"] = uploadedTxt?.Uri,
                ["transcriptFileName"] = uploadedTxt?.Name,
                ["transcriptFileMimeType"] = uploadedTxt?.MimeType
            },
            ["raw"] = raw
        };
    }

    private static string ExtractTranscript(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("transcript", out var transcript)
                && transcript.ValueKind == JsonValueKind.String)
                return transcript.GetString() ?? string.Empty;

            return json;
        }
        catch
        {
            return json;
        }
    }

    private static string NormalizeSpeechToTextModel(string? value)
    {
        var model = (value ?? "saarika:v2.5").Trim();
        return model is "saarika:v2.5" or "saaras:v3" ? model : "saarika:v2.5";
    }

    private static string NormalizeSpeechToTextTranslateModel(string? value)
    {
        var model = (value ?? "saaras:v2.5").Trim();
        return model is "saaras:v2.5" ? model : "saaras:v2.5";
    }

    private static string NormalizeLanguageCode(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var mode = value.Trim().ToLowerInvariant();
        return mode is "transcribe" or "translate" or "verbatim" or "translit" or "codemix"
            ? mode
            : null;
    }

    [Description("Please fill in the Sarvam speech-to-text request.")]
    public sealed class SarvamSpeechToTextRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("saarika:v2.5 or saaras:v3.")]
        public string Model { get; set; } = "saarika:v2.5";

        [JsonPropertyName("mode")]
        [Description("Mode for saaras:v3: transcribe, translate, verbatim, translit, codemix.")]
        public string? Mode { get; set; }

        [JsonPropertyName("language_code")]
        [Required]
        [Description("Language code in BCP-47 (use unknown for autodetect).")]
        public string LanguageCode { get; set; } = "unknown";

        [JsonPropertyName("input_audio_codec")]
        [Description("Optional input audio codec.")]
        public string? InputAudioCodec { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Sarvam speech-to-text-translate request.")]
    public sealed class SarvamSpeechToTextTranslateRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio file URL to transcribe+translate.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Description("Optional prompt context.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Model. Default: saaras:v2.5.")]
        public string Model { get; set; } = "saaras:v2.5";

        [JsonPropertyName("input_audio_codec")]
        [Description("Optional input audio codec.")]
        public string? InputAudioCodec { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

