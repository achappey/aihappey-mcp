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

namespace MCPhappey.Tools.Morpheus;

public static class MorpheusSpeech
{
    private const string SpeechUrl = "https://api.mor.org/api/v1/audio/speech";

    [Description("Generate speech audio from raw text using Morpheus and upload the result as a resource link block.")]
    [McpServerTool(
        Title = "Morpheus Text-to-Speech",
        Name = "morpheus_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Morpheus_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech (max 10,000 chars).")]
        string input,
        [Description("Model ID (required).")]
        string model,
        [Description("Voice ID. Default: af_alloy.")]
        string voice = "af_alloy",
        [Description("Output format: mp3, opus, aac, flac, wav, pcm. Default: mp3.")]
        string response_format = "mp3",
        [Description("Speech speed (0.25 to 4.0). Default: 1.0")]
        double speed = 1.0,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new MorpheusSpeechTextToSpeechRequest
                {
                    Input = input,
                    Model = model,
                    Voice = string.IsNullOrWhiteSpace(voice) ? "af_alloy" : voice.Trim(),
                    ResponseFormat = NormalizeResponseFormat(response_format),
                    Speed = ClampSpeed(speed),
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
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Morpheus.")]
    [McpServerTool(
        Title = "Morpheus File-to-Speech",
        Name = "morpheus_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Morpheus_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("Model ID (required).")]
        string model,
        [Description("Voice ID. Default: af_alloy.")]
        string voice = "af_alloy",
        [Description("Output format: mp3, opus, aac, flac, wav, pcm. Default: mp3.")]
        string response_format = "mp3",
        [Description("Speech speed (0.25 to 4.0). Default: 1.0")]
        double speed = 1.0,
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
                new MorpheusSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = model,
                    Voice = string.IsNullOrWhiteSpace(voice) ? "af_alloy" : voice.Trim(),
                    ResponseFormat = NormalizeResponseFormat(response_format),
                    Speed = ClampSpeed(speed),
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
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateInput(input, model, voice);

        var settings = serviceProvider.GetRequiredService<MorpheusSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var resolvedFormat = NormalizeResponseFormat(responseFormat);

        var payload = new
        {
            input,
            model,
            voice,
            response_format = resolvedFormat,
            speed = ClampSpeed(speed)
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
            throw new InvalidOperationException($"Morpheus speech call failed ({(int)resp.StatusCode}): {body}");
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
        => Math.Clamp(speed, 0.25, 4.0);

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

    [Description("Please fill in the Morpheus text-to-speech request.")]
    public sealed class MorpheusSpeechTextToSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text to synthesize (max 10,000 chars).")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Morpheus model ID (required).")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice ID. Default: af_alloy.")]
        public string Voice { get; set; } = "af_alloy";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output format: mp3, opus, aac, flac, wav, pcm.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Morpheus file-to-speech request.")]
    public sealed class MorpheusSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Morpheus model ID (required).")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice ID. Default: af_alloy.")]
        public string Voice { get; set; } = "af_alloy";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output format: mp3, opus, aac, flac, wav, pcm.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("speed")]
        [Description("Speech speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}
