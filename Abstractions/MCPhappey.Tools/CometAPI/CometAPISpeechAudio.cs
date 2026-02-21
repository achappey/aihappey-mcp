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

namespace MCPhappey.Tools.CometAPI;

public static class CometAPISpeechAudio
{
    private const string SpeechUrl = "https://api.cometapi.com/v1/audio/speech";
    private static readonly HashSet<string> AllowedModels =
    [
        "tts-1",
        "tts-1-hd"
    ];

    private static readonly HashSet<string> AllowedVoices =
    [
        "alloy",
        "echo",
        "fable",
        "onyx",
        "nova",
        "shimmer"
    ];

    [Description("Generate speech audio from raw text using CometAPI and upload the result as a resource link.")]
    [McpServerTool(
        Title = "CometAPI Text-to-Speech",
        Name = "cometapi_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> CometAPI_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string input,
        [Description("TTS model: tts-1 or tts-1-hd. Default: tts-1.")] string model = "tts-1",
        [Description("Voice: alloy, echo, fable, onyx, nova, shimmer. Default: alloy.")] string voice = "alloy",
        [Description("Speed between 0.25 and 4.0. Default: 1.0.")] double speed = 1.0,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CometAPISpeechTextToSpeechRequest
                {
                    Input = input,
                    Model = NormalizeModel(model),
                    Voice = NormalizeVoice(voice),
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
                typed.Speed,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with CometAPI.")]
    [McpServerTool(
        Title = "CometAPI File-to-Speech",
        Name = "cometapi_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> CometAPI_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("TTS model: tts-1 or tts-1-hd. Default: tts-1.")] string model = "tts-1",
        [Description("Voice: alloy, echo, fable, onyx, nova, shimmer. Default: alloy.")] string voice = "alloy",
        [Description("Speed between 0.25 and 4.0. Default: 1.0.")] double speed = 1.0,
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
                new CometAPISpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    Voice = NormalizeVoice(voice),
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
        double speed,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var settings = serviceProvider.GetRequiredService<CometAPISettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var payload = new
        {
            model = NormalizeModel(model),
            input = input.Length > 4096 ? input[..4096] : input,
            voice = NormalizeVoice(voice),
            response_format = "mp3",
            speed = ClampSpeed(speed).ToString("0.##")
        };

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"CometAPI speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var uploadName = filename.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.mp3";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "tts-1").Trim().ToLowerInvariant();
        return AllowedModels.Contains(value) ? value : "tts-1";
    }

    private static string NormalizeVoice(string? voice)
    {
        var value = (voice ?? "alloy").Trim().ToLowerInvariant();
        return AllowedVoices.Contains(value) ? value : "alloy";
    }

    private static double ClampSpeed(double speed)
        => Math.Clamp(speed, 0.25, 4.0);

    [Description("Please fill in the CometAPI text-to-speech request.")]
    public sealed class CometAPISpeechTextToSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text to synthesize.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("One of: tts-1, tts-1-hd.")]
        public string Model { get; set; } = "tts-1";

        [JsonPropertyName("voice")]
        [Required]
        [Description("One of: alloy, echo, fable, onyx, nova, shimmer.")]
        public string Voice { get; set; } = "alloy";

        [JsonPropertyName("speed")]
        [Description("Speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the CometAPI file-to-speech request.")]
    public sealed class CometAPISpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("One of: tts-1, tts-1-hd.")]
        public string Model { get; set; } = "tts-1";

        [JsonPropertyName("voice")]
        [Required]
        [Description("One of: alloy, echo, fable, onyx, nova, shimmer.")]
        public string Voice { get; set; } = "alloy";

        [JsonPropertyName("speed")]
        [Description("Speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

