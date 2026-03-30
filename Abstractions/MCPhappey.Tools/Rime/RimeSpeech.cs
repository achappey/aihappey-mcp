using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Rime;

public static class RimeSpeech
{
    private const string SpeechUrl = "https://users.rime.ai/v1/rime-tts";

    [Description("Generate speech audio from raw text using Rime and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Rime Text-to-Speech",
        Name = "rime_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Rime_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Rime speaker/voice name.")] string speaker,
        [Description("Rime model ID. Default: mist.")] string modelId = "mist",
        [Description("Language code. Default: eng.")] string lang = "eng",
        [Description("Audio format: mp3, wav, ogg, mulaw. Default: mp3.")] string audioFormat = "mp3",
        [Description("Comma-separated speed values for bracketed words.")] string? inlineSpeedAlpha = null,
        [Description("Sampling rate. Required for ogg; optional otherwise.")] int? samplingRate = null,
        [Description("Speech speed multiplier. Default: 1.0.")] double speedAlpha = 1.0,
        [Description("Skip text normalization before synthesis.")] bool noTextNormalization = false,
        [Description("Insert pauses for angle-bracket tags.")] bool pauseBetweenBrackets = false,
        [Description("Enable phonemes inside curly brackets.")] bool phonemizeBetweenBrackets = false,
        [Description("Save out-of-vocabulary words for review.")] bool saveOovs = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                text,
                speaker,
                modelId,
                lang,
                audioFormat,
                inlineSpeedAlpha,
                samplingRate,
                speedAlpha,
                noTextNormalization,
                pauseBetweenBrackets,
                phonemizeBetweenBrackets,
                saveOovs,
                filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Rime.")]
    [McpServerTool(
        Title = "Rime File-to-Speech",
        Name = "rime_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Rime_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Rime speaker/voice name.")] string speaker,
        [Description("Rime model ID. Default: mist.")] string modelId = "mist",
        [Description("Language code. Default: eng.")] string lang = "eng",
        [Description("Audio format: mp3, wav, ogg, mulaw. Default: mp3.")] string audioFormat = "mp3",
        [Description("Comma-separated speed values for bracketed words.")] string? inlineSpeedAlpha = null,
        [Description("Sampling rate. Required for ogg; optional otherwise.")] int? samplingRate = null,
        [Description("Speech speed multiplier. Default: 1.0.")] double speedAlpha = 1.0,
        [Description("Skip text normalization before synthesis.")] bool noTextNormalization = false,
        [Description("Insert pauses for angle-bracket tags.")] bool pauseBetweenBrackets = false,
        [Description("Enable phonemes inside curly brackets.")] bool phonemizeBetweenBrackets = false,
        [Description("Save out-of-vocabulary words for review.")] bool saveOovs = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(sourceText))
                throw new InvalidOperationException("No readable text content found in fileUrl.");

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                speaker,
                modelId,
                lang,
                audioFormat,
                inlineSpeedAlpha,
                samplingRate,
                speedAlpha,
                noTextNormalization,
                pauseBetweenBrackets,
                phonemizeBetweenBrackets,
                saveOovs,
                filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string speaker,
        string modelId,
        string lang,
        string audioFormat,
        string? inlineSpeedAlpha,
        int? samplingRate,
        double speedAlpha,
        bool noTextNormalization,
        bool pauseBetweenBrackets,
        bool phonemizeBetweenBrackets,
        bool saveOovs,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateRequest(text, speaker, audioFormat, samplingRate, speedAlpha);

        var settings = serviceProvider.GetRequiredService<RimeSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var normalizedFormat = NormalizeAudioFormat(audioFormat);

        var payload = new Dictionary<string, object?>
        {
            ["speaker"] = speaker.Trim(),
            ["text"] = text,
            ["modelId"] = NormalizeModelId(modelId),
            ["lang"] = NormalizeLanguage(lang),
            ["audioFormat"] = normalizedFormat,
            ["speedAlpha"] = speedAlpha,
            ["noTextNormalization"] = noTextNormalization,
            ["pauseBetweenBrackets"] = pauseBetweenBrackets,
            ["phonemizeBetweenBrackets"] = phonemizeBetweenBrackets,
            ["saveOovs"] = saveOovs
        };

        if (!string.IsNullOrWhiteSpace(inlineSpeedAlpha))
            payload["inlineSpeedAlpha"] = inlineSpeedAlpha.Trim();

        if (samplingRate.HasValue)
            payload["samplingRate"] = samplingRate.Value;

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Rime speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var responseContentType = resp.Content.Headers.ContentType?.MediaType?.Trim().ToLowerInvariant();
        byte[] audioBytes;
        string extension;

        if (responseContentType == "application/json" || LooksLikeJson(bytes))
        {
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var base64 = TryGetString(root, "audioContent")
                ?? TryGetString(root, "audio_data")
                ?? TryGetString(root, "data")
                ?? TryGetString(root, "audio");

            if (string.IsNullOrWhiteSpace(base64))
                throw new InvalidOperationException("Rime response did not include base64 audio content.");

            audioBytes = Convert.FromBase64String(base64);
            extension = normalizedFormat;
        }
        else
        {
            audioBytes = bytes;
            extension = ResolveExtension(responseContentType, normalizedFormat);
        }

        var uploadName = filename.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{extension}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(audioBytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateRequest(string text, string speaker, string audioFormat, int? samplingRate, double speedAlpha)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        if (text.Length > 10000)
            throw new ValidationException("text exceeds maximum length of 10,000 characters.");

        if (string.IsNullOrWhiteSpace(speaker))
            throw new ValidationException("speaker is required.");

        var normalizedFormat = NormalizeAudioFormat(audioFormat);
        if (normalizedFormat == "ogg")
        {
            if (!samplingRate.HasValue)
                throw new ValidationException("samplingRate is required when audioFormat is ogg.");

            var valid = samplingRate.Value is 8000 or 12000 or 16000 or 24000;
            if (!valid)
                throw new ValidationException("samplingRate for ogg must be one of 8000, 12000, 16000, or 24000.");
        }
        else if (samplingRate.HasValue && (samplingRate.Value < 4000 || samplingRate.Value > 44100))
        {
            throw new ValidationException("samplingRate must be between 4000 and 44100.");
        }

        if (speedAlpha <= 0)
            throw new ValidationException("speedAlpha must be greater than 0.");
    }

    private static string NormalizeAudioFormat(string? value)
    {
        var format = (value ?? "mp3").Trim().ToLowerInvariant();
        return format is "mp3" or "wav" or "ogg" or "mulaw" ? format : "mp3";
    }

    private static string NormalizeModelId(string? value)
    {
        var model = (value ?? "mist").Trim().ToLowerInvariant();
        return model is "mist" or "mistv2" or "arcana" ? model : "mist";
    }

    private static string NormalizeLanguage(string? value)
    {
        var lang = (value ?? "eng").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(lang) ? "eng" : lang;
    }

    private static bool LooksLikeJson(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart();
        return text.StartsWith("{") || text.StartsWith("[");
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string ResolveExtension(string? mimeType, string fallback)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/basic" => "mulaw",
            "audio/mulaw" => "mulaw",
            _ => fallback
        };
    }

    [Description("Please fill in the Rime text-to-speech request.")]
    public sealed class RimeTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("speaker")]
        [Required]
        [Description("Rime speaker/voice name.")]
        public string Speaker { get; set; } = default!;

        [JsonPropertyName("modelId")]
        [Required]
        [Description("Rime model ID. Default: mist.")]
        public string ModelId { get; set; } = "mist";

        [JsonPropertyName("lang")]
        [Required]
        [Description("Language code. Default: eng.")]
        public string Lang { get; set; } = "eng";

        [JsonPropertyName("audioFormat")]
        [Required]
        [Description("Audio format: mp3, wav, ogg, mulaw.")]
        public string AudioFormat { get; set; } = "mp3";

        [JsonPropertyName("inlineSpeedAlpha")]
        [Description("Comma-separated speed values for bracketed words.")]
        public string? InlineSpeedAlpha { get; set; }

        [JsonPropertyName("samplingRate")]
        [Description("Sampling rate. Required for ogg; optional otherwise.")]
        public int? SamplingRate { get; set; }

        [JsonPropertyName("speedAlpha")]
        [Description("Speech speed multiplier.")]
        public double SpeedAlpha { get; set; } = 1.0;

        [JsonPropertyName("noTextNormalization")]
        [Description("Skip text normalization before synthesis.")]
        public bool NoTextNormalization { get; set; } = false;

        [JsonPropertyName("pauseBetweenBrackets")]
        [Description("Insert pauses for angle-bracket tags.")]
        public bool PauseBetweenBrackets { get; set; } = false;

        [JsonPropertyName("phonemizeBetweenBrackets")]
        [Description("Enable phonemes inside curly brackets.")]
        public bool PhonemizeBetweenBrackets { get; set; } = false;

        [JsonPropertyName("saveOovs")]
        [Description("Save out-of-vocabulary words for review.")]
        public bool SaveOovs { get; set; } = false;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Rime file-to-speech request.")]
    public sealed class RimeFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("speaker")]
        [Required]
        [Description("Rime speaker/voice name.")]
        public string Speaker { get; set; } = default!;

        [JsonPropertyName("modelId")]
        [Required]
        [Description("Rime model ID. Default: mist.")]
        public string ModelId { get; set; } = "mist";

        [JsonPropertyName("lang")]
        [Required]
        [Description("Language code. Default: eng.")]
        public string Lang { get; set; } = "eng";

        [JsonPropertyName("audioFormat")]
        [Required]
        [Description("Audio format: mp3, wav, ogg, mulaw.")]
        public string AudioFormat { get; set; } = "mp3";

        [JsonPropertyName("inlineSpeedAlpha")]
        [Description("Comma-separated speed values for bracketed words.")]
        public string? InlineSpeedAlpha { get; set; }

        [JsonPropertyName("samplingRate")]
        [Description("Sampling rate. Required for ogg; optional otherwise.")]
        public int? SamplingRate { get; set; }

        [JsonPropertyName("speedAlpha")]
        [Description("Speech speed multiplier.")]
        public double SpeedAlpha { get; set; } = 1.0;

        [JsonPropertyName("noTextNormalization")]
        [Description("Skip text normalization before synthesis.")]
        public bool NoTextNormalization { get; set; } = false;

        [JsonPropertyName("pauseBetweenBrackets")]
        [Description("Insert pauses for angle-bracket tags.")]
        public bool PauseBetweenBrackets { get; set; } = false;

        [JsonPropertyName("phonemizeBetweenBrackets")]
        [Description("Enable phonemes inside curly brackets.")]
        public bool PhonemizeBetweenBrackets { get; set; } = false;

        [JsonPropertyName("saveOovs")]
        [Description("Save out-of-vocabulary words for review.")]
        public bool SaveOovs { get; set; } = false;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}
