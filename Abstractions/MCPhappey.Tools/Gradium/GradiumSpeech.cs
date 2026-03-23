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

namespace MCPhappey.Tools.Gradium;

public static class GradiumSpeech
{
    private const string SpeechUrl = "https://eu.api.gradium.ai/api/post/speech/tts";

    [Description("Generate speech audio from raw text using Gradium, upload the result, and return only a resource link block.")]
    [McpServerTool(
        Title = "Gradium Text-to-Speech",
        Name = "gradium_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Gradium voice ID to use for synthesis.")] string voice_id,
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000. Default: wav.")] string output_format = "wav",
        [Description("Optional additional JSON config string.")] string? json_config = null,
        [Description("Optional TTS model name. Default: default.")] string model_name = "default",
        [Description("Return raw audio bytes. Default: true.")] bool only_audio = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GradiumTextToSpeechRequest
                {
                    Text = text,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    JsonConfig = string.IsNullOrWhiteSpace(json_config) ? null : json_config.Trim(),
                    ModelName = string.IsNullOrWhiteSpace(model_name) ? "default" : model_name.Trim(),
                    OnlyAudio = only_audio,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.VoiceId,
                typed.OutputFormat,
                typed.JsonConfig,
                typed.ModelName,
                typed.OnlyAudio,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first with Gradium, upload the result, and return only a resource link block.")]
    [McpServerTool(
        Title = "Gradium File-to-Speech",
        Name = "gradium_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Gradium_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Gradium voice ID to use for synthesis.")] string voice_id,
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000. Default: wav.")] string output_format = "wav",
        [Description("Optional additional JSON config string.")] string? json_config = null,
        [Description("Optional TTS model name. Default: default.")] string model_name = "default",
        [Description("Return raw audio bytes. Default: true.")] bool only_audio = true,
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
                new GradiumFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = voice_id,
                    OutputFormat = NormalizeOutputFormat(output_format),
                    JsonConfig = string.IsNullOrWhiteSpace(json_config) ? null : json_config.Trim(),
                    ModelName = string.IsNullOrWhiteSpace(model_name) ? "default" : model_name.Trim(),
                    OnlyAudio = only_audio,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.VoiceId,
                typed.OutputFormat,
                typed.JsonConfig,
                typed.ModelName,
                typed.OnlyAudio,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string voiceId,
        string outputFormat,
        string? jsonConfig,
        string modelName,
        bool onlyAudio,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateSpeechRequest(text, voiceId);

        var settings = serviceProvider.GetRequiredService<GradiumSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var normalizedFormat = NormalizeOutputFormat(outputFormat);

        var payload = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["voice_id"] = voiceId.Trim(),
            ["output_format"] = normalizedFormat,
            ["only_audio"] = onlyAudio
        };

        if (!string.IsNullOrWhiteSpace(jsonConfig))
            payload["json_config"] = jsonConfig.Trim();

        if (!string.IsNullOrWhiteSpace(modelName))
            payload["model_name"] = modelName.Trim();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Add("x-api-key", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GetAcceptMimeType(normalizedFormat)));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Gradium speech call failed ({(int)resp.StatusCode}): {body}");
        }

        var extension = ResolveExtension(resp.Content.Headers.ContentType?.MediaType, normalizedFormat);
        var uploadName = filename.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{extension}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateSpeechRequest(string text, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        if (string.IsNullOrWhiteSpace(voiceId))
            throw new ValidationException("voice_id is required.");
    }

    private static string NormalizeOutputFormat(string? outputFormat)
    {
        var value = (outputFormat ?? "wav").Trim().ToLowerInvariant();
        return value is "wav" or "pcm" or "opus" or "ulaw_8000" or "alaw_8000" or "pcm_8000" or "pcm_16000" or "pcm_24000"
            ? value
            : "wav";
    }

    private static string GetAcceptMimeType(string outputFormat)
        => outputFormat switch
        {
            "opus" => "audio/ogg",
            "pcm" or "pcm_8000" or "pcm_16000" or "pcm_24000" => "audio/pcm",
            "ulaw_8000" or "alaw_8000" => "audio/basic",
            _ => "audio/wav"
        };

    private static string ResolveExtension(string? mimeType, string fallbackFormat)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/pcm" => "pcm",
            "audio/basic" => fallbackFormat is "alaw_8000" ? "alaw" : "ulaw",
            _ => fallbackFormat switch
            {
                "opus" => "ogg",
                "pcm" or "pcm_8000" or "pcm_16000" or "pcm_24000" => "pcm",
                "ulaw_8000" => "ulaw",
                "alaw_8000" => "alaw",
                _ => "wav"
            }
        };
    }

    [Description("Please fill in the Gradium text-to-speech request.")]
    public sealed class GradiumTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Gradium voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("json_config")]
        [Description("Optional additional JSON config string.")]
        public string? JsonConfig { get; set; }

        [JsonPropertyName("model_name")]
        [Description("Optional TTS model name.")]
        public string ModelName { get; set; } = "default";

        [JsonPropertyName("only_audio")]
        [Description("Whether to return only raw audio bytes.")]
        public bool OnlyAudio { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Gradium file-to-speech request.")]
    public sealed class GradiumFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Gradium voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output format: wav, pcm, opus, ulaw_8000, alaw_8000, pcm_8000, pcm_16000, pcm_24000.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("json_config")]
        [Description("Optional additional JSON config string.")]
        public string? JsonConfig { get; set; }

        [JsonPropertyName("model_name")]
        [Description("Optional TTS model name.")]
        public string ModelName { get; set; } = "default";

        [JsonPropertyName("only_audio")]
        [Description("Whether to return only raw audio bytes.")]
        public bool OnlyAudio { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

}
