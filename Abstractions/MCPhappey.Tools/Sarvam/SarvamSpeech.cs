using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Sarvam;

public static class SarvamSpeech
{
    private const string TextToSpeechUrl = "https://api.sarvam.ai/text-to-speech";

    [Description("Generate speech audio from raw text using Sarvam and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Sarvam Text-to-Speech",
        Name = "sarvam_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Sarvam_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Target language code (BCP-47), e.g. en-IN, hi-IN, ta-IN. Default: en-IN.")] string target_language_code = "en-IN",
        [Description("Optional speaker voice name.")] string? speaker = null,
        [Description("Optional pitch. Supported for bulbul:v2 only.")] double? pitch = null,
        [Description("Optional pace. bulbul:v3: 0.5-2.0, bulbul:v2: 0.3-3.0.")] double? pace = null,
        [Description("Optional loudness. Supported for bulbul:v2 only.")] double? loudness = null,
        [Description("Optional sample rate: 8000, 16000, 22050, 24000, 32000, 44100, 48000.")] string? speech_sample_rate = null,
        [Description("Enable text preprocessing. Supported for bulbul:v2.")] bool enable_preprocessing = false,
        [Description("Model: bulbul:v2 or bulbul:v3. Default: bulbul:v3.")] string model = "bulbul:v3",
        [Description("Output audio codec: mp3, linear16, mulaw, alaw, opus, flac, aac, wav. Default: wav.")] string output_audio_codec = "wav",
        [Description("Optional temperature. Supported for bulbul:v3.")] double? temperature = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SarvamSpeechTextToSpeechRequest
                {
                    Text = text,
                    TargetLanguageCode = NormalizeLanguageCode(target_language_code),
                    Speaker = NormalizeOptional(speaker),
                    Pitch = pitch,
                    Pace = pace,
                    Loudness = loudness,
                    SpeechSampleRate = NormalizeSampleRate(speech_sample_rate),
                    EnablePreprocessing = enable_preprocessing,
                    Model = NormalizeModel(model),
                    OutputAudioCodec = NormalizeOutputCodec(output_audio_codec),
                    Temperature = temperature,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.TargetLanguageCode,
                typed.Speaker,
                typed.Pitch,
                typed.Pace,
                typed.Loudness,
                typed.SpeechSampleRate,
                typed.EnablePreprocessing,
                typed.Model,
                typed.OutputAudioCodec,
                typed.Temperature,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Sarvam.")]
    [McpServerTool(
        Title = "Sarvam File-to-Speech",
        Name = "sarvam_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Sarvam_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Target language code (BCP-47), e.g. en-IN, hi-IN, ta-IN. Default: en-IN.")] string target_language_code = "en-IN",
        [Description("Optional speaker voice name.")] string? speaker = null,
        [Description("Optional pitch. Supported for bulbul:v2 only.")] double? pitch = null,
        [Description("Optional pace. bulbul:v3: 0.5-2.0, bulbul:v2: 0.3-3.0.")] double? pace = null,
        [Description("Optional loudness. Supported for bulbul:v2 only.")] double? loudness = null,
        [Description("Optional sample rate: 8000, 16000, 22050, 24000, 32000, 44100, 48000.")] string? speech_sample_rate = null,
        [Description("Enable text preprocessing. Supported for bulbul:v2.")] bool enable_preprocessing = false,
        [Description("Model: bulbul:v2 or bulbul:v3. Default: bulbul:v3.")] string model = "bulbul:v3",
        [Description("Output audio codec: mp3, linear16, mulaw, alaw, opus, flac, aac, wav. Default: wav.")] string output_audio_codec = "wav",
        [Description("Optional temperature. Supported for bulbul:v3.")] double? temperature = null,
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

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SarvamSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    TargetLanguageCode = NormalizeLanguageCode(target_language_code),
                    Speaker = NormalizeOptional(speaker),
                    Pitch = pitch,
                    Pace = pace,
                    Loudness = loudness,
                    SpeechSampleRate = NormalizeSampleRate(speech_sample_rate),
                    EnablePreprocessing = enable_preprocessing,
                    Model = NormalizeModel(model),
                    OutputAudioCodec = NormalizeOutputCodec(output_audio_codec),
                    Temperature = temperature,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.TargetLanguageCode,
                typed.Speaker,
                typed.Pitch,
                typed.Pace,
                typed.Loudness,
                typed.SpeechSampleRate,
                typed.EnablePreprocessing,
                typed.Model,
                typed.OutputAudioCodec,
                typed.Temperature,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string targetLanguageCode,
        string? speaker,
        double? pitch,
        double? pace,
        double? loudness,
        string? speechSampleRate,
        bool enablePreprocessing,
        string model,
        string outputAudioCodec,
        double? temperature,
        string filename,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        var settings = serviceProvider.GetRequiredService<SarvamSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["target_language_code"] = NormalizeLanguageCode(targetLanguageCode),
            ["model"] = NormalizeModel(model),
            ["output_audio_codec"] = NormalizeOutputCodec(outputAudioCodec),
            ["enable_preprocessing"] = enablePreprocessing
        };

        if (!string.IsNullOrWhiteSpace(speaker)) payload["speaker"] = speaker;
        if (pitch.HasValue) payload["pitch"] = pitch.Value;
        if (pace.HasValue) payload["pace"] = pace.Value;
        if (loudness.HasValue) payload["loudness"] = loudness.Value;
        if (!string.IsNullOrWhiteSpace(speechSampleRate)) payload["speech_sample_rate"] = speechSampleRate;
        if (temperature.HasValue) payload["temperature"] = temperature.Value;

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, TextToSpeechUrl);
        req.Headers.Add("api-subscription-key", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sarvam text-to-speech call failed ({(int)resp.StatusCode}): {json}");

        var bytes = ExtractAudioBytes(json);
        if (bytes.Length == 0)
            throw new InvalidOperationException("Sarvam text-to-speech returned empty audio payload.");

        var ext = GetExtensionFromCodec(outputAudioCodec);
        var uploadName = filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static byte[] ExtractAudioBytes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("audios", out var audios)
            || audios.ValueKind != JsonValueKind.Array
            || audios.GetArrayLength() == 0)
            return [];

        var base64 = audios[0].GetString();
        if (string.IsNullOrWhiteSpace(base64))
            return [];

        return Convert.FromBase64String(base64);
    }

    private static string NormalizeLanguageCode(string? value)
        => string.IsNullOrWhiteSpace(value) ? "en-IN" : value.Trim();

    private static string NormalizeModel(string? value)
    {
        var model = (value ?? "bulbul:v3").Trim();
        return model is "bulbul:v2" or "bulbul:v3" ? model : "bulbul:v3";
    }

    private static string NormalizeOutputCodec(string? value)
    {
        var codec = (value ?? "wav").Trim().ToLowerInvariant();
        return codec is "mp3" or "linear16" or "mulaw" or "alaw" or "opus" or "flac" or "aac" or "wav"
            ? codec
            : "wav";
    }

    private static string? NormalizeSampleRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized is "8000" or "16000" or "22050" or "24000" or "32000" or "44100" or "48000"
            ? normalized
            : null;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetExtensionFromCodec(string codec)
        => NormalizeOutputCodec(codec) switch
        {
            "linear16" => "wav",
            "mulaw" => "mulaw",
            "alaw" => "alaw",
            _ => NormalizeOutputCodec(codec)
        };

    [Description("Please fill in the Sarvam text-to-speech request.")]
    public sealed class SarvamSpeechTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Plain text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("target_language_code")]
        [Required]
        [Description("Language code in BCP-47 format (e.g. en-IN, hi-IN).")]
        public string TargetLanguageCode { get; set; } = "en-IN";

        [JsonPropertyName("speaker")]
        [Description("Optional speaker voice name.")]
        public string? Speaker { get; set; }

        [JsonPropertyName("pitch")]
        [Description("Optional pitch. Supported for bulbul:v2.")]
        public double? Pitch { get; set; }

        [JsonPropertyName("pace")]
        [Description("Optional speaking pace.")]
        public double? Pace { get; set; }

        [JsonPropertyName("loudness")]
        [Description("Optional loudness. Supported for bulbul:v2.")]
        public double? Loudness { get; set; }

        [JsonPropertyName("speech_sample_rate")]
        [Description("Optional sample rate as string.")]
        public string? SpeechSampleRate { get; set; }

        [JsonPropertyName("enable_preprocessing")]
        [Description("Enable text preprocessing.")]
        public bool EnablePreprocessing { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Model: bulbul:v2 or bulbul:v3.")]
        public string Model { get; set; } = "bulbul:v3";

        [JsonPropertyName("output_audio_codec")]
        [Required]
        [Description("Output audio codec.")]
        public string OutputAudioCodec { get; set; } = "wav";

        [JsonPropertyName("temperature")]
        [Description("Optional temperature. Supported for bulbul:v3.")]
        public double? Temperature { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Sarvam file-to-speech request.")]
    public sealed class SarvamSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("target_language_code")]
        [Required]
        [Description("Language code in BCP-47 format (e.g. en-IN, hi-IN).")]
        public string TargetLanguageCode { get; set; } = "en-IN";

        [JsonPropertyName("speaker")]
        [Description("Optional speaker voice name.")]
        public string? Speaker { get; set; }

        [JsonPropertyName("pitch")]
        [Description("Optional pitch. Supported for bulbul:v2.")]
        public double? Pitch { get; set; }

        [JsonPropertyName("pace")]
        [Description("Optional speaking pace.")]
        public double? Pace { get; set; }

        [JsonPropertyName("loudness")]
        [Description("Optional loudness. Supported for bulbul:v2.")]
        public double? Loudness { get; set; }

        [JsonPropertyName("speech_sample_rate")]
        [Description("Optional sample rate as string.")]
        public string? SpeechSampleRate { get; set; }

        [JsonPropertyName("enable_preprocessing")]
        [Description("Enable text preprocessing.")]
        public bool EnablePreprocessing { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Model: bulbul:v2 or bulbul:v3.")]
        public string Model { get; set; } = "bulbul:v3";

        [JsonPropertyName("output_audio_codec")]
        [Required]
        [Description("Output audio codec.")]
        public string OutputAudioCodec { get; set; } = "wav";

        [JsonPropertyName("temperature")]
        [Description("Optional temperature. Supported for bulbul:v3.")]
        public double? Temperature { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

