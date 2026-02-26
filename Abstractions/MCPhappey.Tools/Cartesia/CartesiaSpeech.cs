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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Cartesia;

public static class CartesiaSpeech
{
    private const string TtsBytesPath = "tts/bytes";

    [Description("Generate speech audio from raw text using Cartesia, upload the output file, and return a resource link block.")]
    [McpServerTool(
        Title = "Cartesia Text-to-Speech",
        Name = "cartesia_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Cartesia_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text transcript to synthesize.")] string transcript,
        [Description("Voice ID to use.")] string voiceId,
        [Description("Model ID. Default: sonic-2.")] string modelId = "sonic-2",
        [Description("Output container: wav, mp3, or raw. Default: wav.")] string container = "wav",
        [Description("Sample rate in Hz. Default: 24000.")] int sampleRate = 24000,
        [Description("Raw encoding (only used when container=raw or wav). Default: pcm_s16le.")] string encoding = "pcm_s16le",
        [Description("MP3 bit rate (only used when container=mp3). Default: 128000.")] int bitRate = 128000,
        [Description("Optional language code (e.g. en, nl, de).")]
        string? language = null,
        [Description("Optional generation speed between 0.6 and 1.5. Default: 1.0.")]
        double speed = 1.0,
        [Description("Optional generation volume between 0.5 and 2.0. Default: 1.0.")]
        double volume = 1.0,
        [Description("Optional emotion for sonic-3 style controls.")]
        string? emotion = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CartesiaSpeechRequest
                {
                    Transcript = transcript,
                    VoiceId = voiceId,
                    ModelId = string.IsNullOrWhiteSpace(modelId) ? "sonic-2" : modelId.Trim(),
                    Container = NormalizeContainer(container),
                    SampleRate = NormalizeSampleRate(sampleRate),
                    Encoding = NormalizeEncoding(encoding),
                    BitRate = NormalizeBitRate(bitRate),
                    Language = NormalizeOptional(language),
                    Speed = Clamp(speed, 0.6, 1.5, 1.0),
                    Volume = Clamp(volume, 0.5, 2.0, 1.0),
                    Emotion = NormalizeOptional(emotion),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(serviceProvider, requestContext, typed, cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by extracting text first with Cartesia, upload the output file, and return a resource link block.")]
    [McpServerTool(
        Title = "Cartesia File-to-Speech",
        Name = "cartesia_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Cartesia_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source file URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Voice ID to use.")] string voiceId,
        [Description("Model ID. Default: sonic-2.")] string modelId = "sonic-2",
        [Description("Output container: wav, mp3, or raw. Default: wav.")] string container = "wav",
        [Description("Sample rate in Hz. Default: 24000.")] int sampleRate = 24000,
        [Description("Raw encoding (only used when container=raw or wav). Default: pcm_s16le.")] string encoding = "pcm_s16le",
        [Description("MP3 bit rate (only used when container=mp3). Default: 128000.")] int bitRate = 128000,
        [Description("Optional language code (e.g. en, nl, de).")]
        string? language = null,
        [Description("Optional generation speed between 0.6 and 1.5. Default: 1.0.")]
        double speed = 1.0,
        [Description("Optional generation volume between 0.5 and 2.0. Default: 1.0.")]
        double volume = 1.0,
        [Description("Optional emotion for sonic-3 style controls.")]
        string? emotion = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var downloader = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloader.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

            if (string.IsNullOrWhiteSpace(sourceText))
                throw new InvalidOperationException("No readable text content found in fileUrl.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CartesiaSpeechFileRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = voiceId,
                    ModelId = string.IsNullOrWhiteSpace(modelId) ? "sonic-2" : modelId.Trim(),
                    Container = NormalizeContainer(container),
                    SampleRate = NormalizeSampleRate(sampleRate),
                    Encoding = NormalizeEncoding(encoding),
                    BitRate = NormalizeBitRate(bitRate),
                    Language = NormalizeOptional(language),
                    Speed = Clamp(speed, 0.6, 1.5, 1.0),
                    Volume = Clamp(volume, 0.5, 2.0, 1.0),
                    Emotion = NormalizeOptional(emotion),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                new CartesiaSpeechRequest
                {
                    Transcript = sourceText,
                    VoiceId = typed.VoiceId,
                    ModelId = typed.ModelId,
                    Container = typed.Container,
                    SampleRate = typed.SampleRate,
                    Encoding = typed.Encoding,
                    BitRate = typed.BitRate,
                    Language = typed.Language,
                    Speed = typed.Speed,
                    Volume = typed.Volume,
                    Emotion = typed.Emotion,
                    Filename = typed.Filename
                },
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CartesiaSpeechRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var outputFormat = BuildOutputFormat(request.Container, request.SampleRate, request.Encoding, request.BitRate);

        var payload = new CartesiaTtsPayload
        {
            ModelId = request.ModelId,
            Transcript = request.Transcript,
            Voice = new CartesiaVoiceSpecifier
            {
                Mode = "id",
                Id = request.VoiceId
            },
            Language = request.Language,
            OutputFormat = outputFormat,
            GenerationConfig = new CartesiaGenerationConfig
            {
                Speed = request.Speed,
                Volume = request.Volume,
                Emotion = request.Emotion
            }
        };

        using var client = serviceProvider.CreateCartesiaClient(GetAcceptMimeType(request.Container));
        using var req = new HttpRequestMessage(HttpMethod.Post, TtsBytesPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Cartesia TTS failed ({(int)resp.StatusCode}): {body}");
        }

        var ext = ResolveExtension(resp.Content.Headers.ContentType?.MediaType, request.Container);
        var outputName = request.Filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? request.Filename
            : $"{request.Filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateRequest(CartesiaSpeechRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Transcript))
            throw new ValidationException("transcript is required.");
        if (string.IsNullOrWhiteSpace(request.VoiceId))
            throw new ValidationException("voiceId is required.");
        if (string.IsNullOrWhiteSpace(request.ModelId))
            throw new ValidationException("modelId is required.");
    }

    private static object BuildOutputFormat(string container, int sampleRate, string encoding, int bitRate)
    {
        var normalizedContainer = NormalizeContainer(container);
        return normalizedContainer switch
        {
            "raw" => new { container = "raw", encoding = NormalizeEncoding(encoding), sample_rate = NormalizeSampleRate(sampleRate) },
            "mp3" => new { container = "mp3", sample_rate = NormalizeSampleRate(sampleRate), bit_rate = NormalizeBitRate(bitRate) },
            _ => new { container = "wav", encoding = NormalizeEncoding(encoding), sample_rate = NormalizeSampleRate(sampleRate) }
        };
    }

    private static string NormalizeContainer(string? container)
    {
        var value = (container ?? "wav").Trim().ToLowerInvariant();
        return value is "wav" or "mp3" or "raw" ? value : "wav";
    }

    private static string NormalizeEncoding(string? encoding)
    {
        var value = (encoding ?? "pcm_s16le").Trim().ToLowerInvariant();
        return value is "pcm_f32le" or "pcm_s16le" or "pcm_mulaw" or "pcm_alaw" ? value : "pcm_s16le";
    }

    private static int NormalizeSampleRate(int sampleRate)
        => sampleRate is 8000 or 16000 or 22050 or 24000 or 44100 or 48000 ? sampleRate : 24000;

    private static int NormalizeBitRate(int bitRate)
        => bitRate is 32000 or 64000 or 96000 or 128000 or 192000 ? bitRate : 128000;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double Clamp(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static string GetAcceptMimeType(string container)
        => NormalizeContainer(container) switch
        {
            "raw" => "application/octet-stream",
            "mp3" => "audio/mpeg",
            _ => "audio/wav"
        };

    private static string ResolveExtension(string? mimeType, string fallbackContainer)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "application/octet-stream" => NormalizeContainer(fallbackContainer) == "raw" ? "raw" : NormalizeContainer(fallbackContainer),
            _ => NormalizeContainer(fallbackContainer)
        };
    }

    [Description("Please fill in the Cartesia text-to-speech request.")]
    public sealed class CartesiaSpeechRequest
    {
        [JsonPropertyName("transcript")]
        [Required]
        public string Transcript { get; set; } = default!;

        [JsonPropertyName("voiceId")]
        [Required]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("modelId")]
        [Required]
        public string ModelId { get; set; } = "sonic-2";

        [JsonPropertyName("container")]
        [Required]
        public string Container { get; set; } = "wav";

        [JsonPropertyName("sampleRate")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("encoding")]
        public string Encoding { get; set; } = "pcm_s16le";

        [JsonPropertyName("bitRate")]
        public int BitRate { get; set; } = 128000;

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("volume")]
        public double Volume { get; set; } = 1.0;

        [JsonPropertyName("emotion")]
        public string? Emotion { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Cartesia file-to-speech request.")]
    public sealed class CartesiaSpeechFileRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voiceId")]
        [Required]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("modelId")]
        [Required]
        public string ModelId { get; set; } = "sonic-2";

        [JsonPropertyName("container")]
        [Required]
        public string Container { get; set; } = "wav";

        [JsonPropertyName("sampleRate")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("encoding")]
        public string Encoding { get; set; } = "pcm_s16le";

        [JsonPropertyName("bitRate")]
        public int BitRate { get; set; } = 128000;

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("volume")]
        public double Volume { get; set; } = 1.0;

        [JsonPropertyName("emotion")]
        public string? Emotion { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;
    }

    private sealed class CartesiaTtsPayload
    {
        [JsonPropertyName("model_id")]
        public string ModelId { get; set; } = default!;

        [JsonPropertyName("transcript")]
        public string Transcript { get; set; } = default!;

        [JsonPropertyName("voice")]
        public CartesiaVoiceSpecifier Voice { get; set; } = default!;

        [JsonPropertyName("language")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Language { get; set; }

        [JsonPropertyName("generation_config")]
        public CartesiaGenerationConfig GenerationConfig { get; set; } = default!;

        [JsonPropertyName("output_format")]
        public object OutputFormat { get; set; } = default!;
    }

    private sealed class CartesiaVoiceSpecifier
    {
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "id";

        [JsonPropertyName("id")]
        public string Id { get; set; } = default!;
    }

    private sealed class CartesiaGenerationConfig
    {
        [JsonPropertyName("speed")]
        public double Speed { get; set; }

        [JsonPropertyName("volume")]
        public double Volume { get; set; }

        [JsonPropertyName("emotion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Emotion { get; set; }
    }
}

