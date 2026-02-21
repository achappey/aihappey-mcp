using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.CometAPI;

public static class CometAPITranscriptions
{
    private const string TranscriptionsUrl = "https://api.cometapi.com/v1/audio/transcriptions";
    private const string TranslationsUrl = "https://api.cometapi.com/v1/audio/translations";

    [Description("Create an audio transcription from fileUrl using CometAPI and return structured text output with uploaded .txt artifact.")]
    [McpServerTool(
        Title = "CometAPI Audio Transcription (fileUrl)",
        Name = "cometapi_audio_transcription_fileurl",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> CometAPI_Audio_Transcription_FileUrl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to transcribe (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Model to use. Default: whisper-1.")]
        string model = "whisper-1",
        [Description("Optional text prompt to guide transcription.")]
        string? prompt = null,
        [Description("Optional language hint in ISO-639-1 (e.g. en, nl).")]
        string? language = null,
        [Description("Response format: json, text, srt, verbose_json, or vtt. Default: json.")]
        string responseFormat = "json",
        [Description("Sampling temperature between 0 and 1. Default: 0.")]
        double temperature = 0,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CometAPITranscriptionFromFileUrlRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    Prompt = prompt,
                    Language = language,
                    ResponseFormat = NormalizeResponseFormat(responseFormat),
                    Temperature = ClampTemperature(temperature),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await ExecuteAudioFileOperationAsync(
                serviceProvider,
                requestContext,
                endpointUrl: TranscriptionsUrl,
                typed.FileUrl,
                typed.Model,
                typed.Prompt,
                typed.Language,
                typed.ResponseFormat,
                typed.Temperature,
                typed.Filename,
                cancellationToken);
        });

    [Description("Create an audio translation from fileUrl using CometAPI and return structured text output with uploaded .txt artifact.")]
    [McpServerTool(
        Title = "CometAPI Audio Translation (fileUrl)",
        Name = "cometapi_audio_translation_fileurl",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> CometAPI_Audio_Translation_FileUrl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to translate (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Model to use. Default: whisper-1.")]
        string model = "whisper-1",
        [Description("Optional text prompt to guide translation.")]
        string? prompt = null,
        [Description("Response format: json, text, srt, verbose_json, or vtt. Default: json.")]
        string responseFormat = "json",
        [Description("Sampling temperature between 0 and 1. Default: 0.")]
        double temperature = 0,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CometAPITranslationFromFileUrlRequest
                {
                    FileUrl = fileUrl,
                    Model = NormalizeModel(model),
                    Prompt = prompt,
                    ResponseFormat = NormalizeResponseFormat(responseFormat),
                    Temperature = ClampTemperature(temperature),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await ExecuteAudioFileOperationAsync(
                serviceProvider,
                requestContext,
                endpointUrl: TranslationsUrl,
                typed.FileUrl,
                typed.Model,
                typed.Prompt,
                language: null,
                typed.ResponseFormat,
                typed.Temperature,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> ExecuteAudioFileOperationAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpointUrl,
        string fileUrl,
        string model,
        string? prompt,
        string? language,
        string responseFormat,
        double temperature,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

        var settings = serviceProvider.GetRequiredService<CometAPISettings>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var downloads = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var mediaFile = downloads.FirstOrDefault()
            ?? throw new InvalidOperationException("Failed to download audio/video content from fileUrl.");

        using var multipart = new MultipartFormDataContent
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
            "model".NamedField(NormalizeModel(model)),
            "response_format".NamedField(NormalizeResponseFormat(responseFormat)),
            "temperature".NamedField(ClampTemperature(temperature).ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrWhiteSpace(prompt))
            multipart.Add("prompt".NamedField(prompt));

        if (!string.IsNullOrWhiteSpace(language))
            multipart.Add("language".NamedField(language));

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
        {
            Content = multipart
        };

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"CometAPI audio call failed ({(int)resp.StatusCode}): {body}");

        var text = ExtractText(body, responseFormat);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No transcription/translation text found in CometAPI response.");

        var uploadName = filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.txt";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromString(text),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                text.ToTextContentBlock(),
                uploaded!
            ]
        };
    }

    private static string NormalizeModel(string? model)
        => string.IsNullOrWhiteSpace(model) ? "whisper-1" : model.Trim();

    private static double ClampTemperature(double temperature)
        => Math.Clamp(temperature, 0, 1);

    private static string NormalizeResponseFormat(string? responseFormat)
    {
        var value = string.IsNullOrWhiteSpace(responseFormat)
            ? "json"
            : responseFormat.Trim().ToLowerInvariant();

        return value switch
        {
            "json" or "text" or "srt" or "verbose_json" or "vtt" => value,
            _ => "json"
        };
    }

    private static string ExtractText(string responseBody, string responseFormat)
    {
        if (!string.Equals(NormalizeResponseFormat(responseFormat), "json", StringComparison.OrdinalIgnoreCase))
            return responseBody;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString() ?? string.Empty;

            return responseBody;
        }
        catch
        {
            return responseBody;
        }
    }

    [Description("Please fill in the CometAPI transcription request from fileUrl.")]
    public sealed class CometAPITranscriptionFromFileUrlRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio/video file URL to transcribe.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model to use. Default: whisper-1.")]
        public string Model { get; set; } = "whisper-1";

        [JsonPropertyName("prompt")]
        [Description("Optional text prompt to guide transcription.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("language")]
        [Description("Optional language hint in ISO-639-1.")]
        public string? Language { get; set; }

        [JsonPropertyName("response_format")]
        [Required]
        [Description("json, text, srt, verbose_json, or vtt.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Description("Sampling temperature between 0 and 1.")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the CometAPI translation request from fileUrl.")]
    public sealed class CometAPITranslationFromFileUrlRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Audio/video file URL to translate.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model to use. Default: whisper-1.")]
        public string Model { get; set; } = "whisper-1";

        [JsonPropertyName("prompt")]
        [Description("Optional text prompt to guide translation.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("response_format")]
        [Required]
        [Description("json, text, srt, verbose_json, or vtt.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Description("Sampling temperature between 0 and 1.")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

