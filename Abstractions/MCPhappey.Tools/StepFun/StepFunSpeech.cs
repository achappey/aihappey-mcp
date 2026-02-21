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

namespace MCPhappey.Tools.StepFun;

public static class StepFunSpeech
{
    private const string SpeechUrl = "https://api.stepfun.ai/v1/audio/speech";

    [Description("Generate speech audio from raw text using StepFun and upload the result as a resource link.")]
    [McpServerTool(
        Title = "StepFun Text-to-Speech",
        Name = "stepfun_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> StepFun_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech (max 10,000 chars).")]
        string input,
        [Description("Model ID. Default: step-tts-2.")]
        string model = "step-tts-2",
        [Description("Voice ID to use for synthesis.")]
        string voice = "lively-girl",
        [Description("Output format: mp3, opus, aac, flac, wav, pcm. Default: mp3.")]
        string response_format = "mp3",
        [Description("Speech speed (0.5 to 2.0). Default: 1.0")]
        double speed = 1.0,
        [Description("Output volume (0.1 to 2.0). Default: 1.0")]
        double volume = 1.0,
        [Description("Sample rate: 8000, 16000, 22050, 24000. Default: 24000")]
        int sample_rate = 24000,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new StepFunSpeechTextToSpeechRequest
                {
                    Input = input,
                    Model = string.IsNullOrWhiteSpace(model) ? "step-tts-2" : model.Trim(),
                    Voice = string.IsNullOrWhiteSpace(voice) ? "lively-girl" : voice.Trim(),
                    ResponseFormat = NormalizeResponseFormat(response_format),
                    Speed = ClampSpeed(speed),
                    Volume = ClampVolume(volume),
                    SampleRate = NormalizeSampleRate(sample_rate),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Input,
                typed.Model,
                typed.Voice,
                typed.ResponseFormat,
                typed.Speed,
                typed.Volume,
                typed.SampleRate,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with StepFun.")]
    [McpServerTool(
        Title = "StepFun File-to-Speech",
        Name = "stepfun_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> StepFun_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("Model ID. Default: step-tts-2.")]
        string model = "step-tts-2",
        [Description("Voice ID to use for synthesis.")]
        string voice = "lively-girl",
        [Description("Output format: mp3, opus, aac, flac, wav, pcm. Default: mp3.")]
        string response_format = "mp3",
        [Description("Speech speed (0.5 to 2.0). Default: 1.0")]
        double speed = 1.0,
        [Description("Output volume (0.1 to 2.0). Default: 1.0")]
        double volume = 1.0,
        [Description("Sample rate: 8000, 16000, 22050, 24000. Default: 24000")]
        int sample_rate = 24000,
        [Description("Output filename without extension.")]
        string? filename = null,
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
                new StepFunSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = string.IsNullOrWhiteSpace(model) ? "step-tts-2" : model.Trim(),
                    Voice = string.IsNullOrWhiteSpace(voice) ? "lively-girl" : voice.Trim(),
                    ResponseFormat = NormalizeResponseFormat(response_format),
                    Speed = ClampSpeed(speed),
                    Volume = ClampVolume(volume),
                    SampleRate = NormalizeSampleRate(sample_rate),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.Model,
                typed.Voice,
                typed.ResponseFormat,
                typed.Speed,
                typed.Volume,
                typed.SampleRate,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string input,
        string model,
        string voice,
        string responseFormat,
        double speed,
        double volume,
        int sampleRate,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateInput(input, model, voice);

        var settings = serviceProvider.GetRequiredService<StepFunSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var resolvedFormat = NormalizeResponseFormat(responseFormat);

        var payload = new
        {
            model,
            input,
            voice,
            response_format = resolvedFormat,
            speed = ClampSpeed(speed),
            volume = ClampVolume(volume),
            sample_rate = NormalizeSampleRate(sampleRate)
        };

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetAcceptMimeType(resolvedFormat)));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"StepFun speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var ext = ResolveExtension(resp.Content.Headers.ContentType?.MediaType, resolvedFormat);
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

    private static void ValidateInput(string input, string model, string voice)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ValidationException("input is required.");

        if (input.Length > 10000)
            throw new ValidationException("input exceeds maximum length of 10,000 characters.");

        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        if (string.IsNullOrWhiteSpace(voice))
            throw new ValidationException("voice is required.");
    }

    private static string NormalizeResponseFormat(string? responseFormat)
    {
        var value = (responseFormat ?? "mp3").Trim().ToLowerInvariant();
        return value is "mp3" or "opus" or "aac" or "flac" or "wav" or "pcm" ? value : "mp3";
    }

    private static double ClampSpeed(double speed)
        => Math.Clamp(speed, 0.5, 2.0);

    private static double ClampVolume(double volume)
        => Math.Clamp(volume, 0.1, 2.0);

    private static int NormalizeSampleRate(int sampleRate)
        => sampleRate is 8000 or 16000 or 22050 or 24000 ? sampleRate : 24000;

    private static string GetAcceptMimeType(string responseFormat)
        => responseFormat switch
        {
            "mp3" => "audio/mpeg",
            "opus" => "audio/opus",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "wav" => "audio/wav",
            "pcm" => "audio/pcm",
            _ => "audio/mpeg"
        };

    private static string ResolveExtension(string? mimeType, string fallbackFormat)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/opus" => "opus",
            "audio/aac" => "aac",
            "audio/flac" => "flac",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/pcm" => "pcm",
            "application/octet-stream" => NormalizeResponseFormat(fallbackFormat),
            _ => NormalizeResponseFormat(fallbackFormat)
        };
    }

    [Description("Please fill in the StepFun text-to-speech request.")]
    public sealed class StepFunSpeechTextToSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text to synthesize (max 10,000 chars).")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("StepFun model ID. Default: step-tts-2.")]
        public string Model { get; set; } = "step-tts-2";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice ID.")]
        public string Voice { get; set; } = "lively-girl";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output format: mp3, opus, aac, flac, wav, pcm.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.5 and 2.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("volume")]
        [Description("Output volume between 0.1 and 2.0.")]
        public double Volume { get; set; } = 1.0;

        [JsonPropertyName("sample_rate")]
        [Description("Sample rate: 8000, 16000, 22050, 24000.")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the StepFun file-to-speech request.")]
    public sealed class StepFunSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("StepFun model ID. Default: step-tts-2.")]
        public string Model { get; set; } = "step-tts-2";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice ID.")]
        public string Voice { get; set; } = "lively-girl";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output format: mp3, opus, aac, flac, wav, pcm.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.5 and 2.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("volume")]
        [Description("Output volume between 0.1 and 2.0.")]
        public double Volume { get; set; } = 1.0;

        [JsonPropertyName("sample_rate")]
        [Description("Sample rate: 8000, 16000, 22050, 24000.")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

