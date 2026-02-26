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

namespace MCPhappey.Tools.FishAudio;

public static class FishAudioSpeech
{
    private const string TtsPath = "/v1/tts";

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Fish Audio and uploading the result as a resource link.")]
    [McpServerTool(
        Title = "Fish Audio File-to-Speech",
        Name = "fishaudio_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> FishAudio_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Model header for Fish Audio TTS. Allowed: s1, speech-1.6, speech-1.5. Default: s1.")] string model = "s1",
        [Description("Voice reference id from Fish Audio model library or custom models.")] string? reference_id = null,
        [Description("Output format: wav, pcm, mp3, opus. Default: mp3.")] string format = "mp3",
        [Description("Expressiveness between 0 and 1. Default: 0.7.")] double temperature = 0.7,
        [Description("Nucleus sampling top_p between 0 and 1. Default: 0.7.")] double top_p = 0.7,
        [Description("Latency mode: low, normal, balanced. Default: normal.")] string latency = "normal",
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
                new FishAudioSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    ReferenceId = NormalizeOptional(reference_id),
                    Format = NormalizeFormat(format),
                    Temperature = Clamp01(temperature, 0.7),
                    TopP = Clamp01(top_p, 0.7),
                    Latency = NormalizeLatency(latency),
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
                typed.ReferenceId,
                typed.Format,
                typed.Temperature,
                typed.TopP,
                typed.Latency,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string model,
        string? referenceId,
        string format,
        double temperature,
        double topP,
        string latency,
        string filename,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text.Length > 15000 ? text[..15000] : text,
            ["temperature"] = Clamp01(temperature, 0.7),
            ["top_p"] = Clamp01(topP, 0.7),
            ["format"] = NormalizeFormat(format),
            ["latency"] = NormalizeLatency(latency)
        };

        if (!string.IsNullOrWhiteSpace(referenceId))
            payload["reference_id"] = referenceId;

        using var client = serviceProvider.CreateFishAudioClient("audio/*");
        using var request = new HttpRequestMessage(HttpMethod.Post, TtsPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json)
        };
        request.Headers.Remove("model");
        request.Headers.Add("model", NormalizeModel(model));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(MimeTypes.Json);

        using var response = await client.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Fish Audio TTS failed ({(int)response.StatusCode}): {body}");
        }

        var ext = NormalizeFormat(format);
        var safeName = filename.ToOutputFileName();
        var outputName = safeName.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? safeName
            : $"{safeName}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "s1").Trim().ToLowerInvariant();
        return value is "s1" or "speech-1.6" or "speech-1.5" ? value : "s1";
    }

    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? "mp3").Trim().ToLowerInvariant();
        return value is "wav" or "pcm" or "mp3" or "opus" ? value : "mp3";
    }

    private static string NormalizeLatency(string? latency)
    {
        var value = (latency ?? "normal").Trim().ToLowerInvariant();
        return value is "low" or "normal" or "balanced" ? value : "normal";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double Clamp01(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback;

        return Math.Clamp(value, 0, 1);
    }

    [Description("Please fill in the Fish Audio file-to-speech request.")]
    public sealed class FishAudioSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("One of: s1, speech-1.6, speech-1.5.")]
        public string Model { get; set; } = "s1";

        [JsonPropertyName("reference_id")]
        [Description("Optional reference model id for voice style.")]
        public string? ReferenceId { get; set; }

        [JsonPropertyName("format")]
        [Required]
        [Description("Output format: wav, pcm, mp3, opus.")]
        public string Format { get; set; } = "mp3";

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Description("Expressiveness between 0 and 1.")]
        public double Temperature { get; set; } = 0.7;

        [JsonPropertyName("top_p")]
        [Range(0, 1)]
        [Description("Nucleus sampling top_p between 0 and 1.")]
        public double TopP { get; set; } = 0.7;

        [JsonPropertyName("latency")]
        [Required]
        [Description("Latency mode: low, normal, balanced.")]
        public string Latency { get; set; } = "normal";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

