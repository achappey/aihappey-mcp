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

namespace MCPhappey.Tools.Verbatik;

public static class VerbatikSpeech
{
    private const string TtsUrl = "https://api.verbatik.com/api/v1/tts";

    [Description("Generate speech audio from raw text using Verbatik and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Verbatik Text-to-Speech",
        Name = "verbatik_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Verbatik_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")]
        string input,
        [Description("Voice ID to use. Default: matthew-en-us.")]
        string voice_id = "matthew-en-us",
        [Description("Whether Verbatik should store audio in S3 and return audio_url. Default: false.")]
        bool store_audio = false,
        [Description("Content type of the input body. Allowed: text/plain, application/ssml+xml. Default: text/plain.")]
        string content_type = "text/plain",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VerbatikTextToSpeechRequest
                {
                    Input = input,
                    VoiceId = string.IsNullOrWhiteSpace(voice_id) ? "matthew-en-us" : voice_id.Trim(),
                    StoreAudio = store_audio,
                    ContentType = NormalizeContentType(content_type),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Input,
                typed.VoiceId,
                typed.StoreAudio,
                typed.ContentType,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Verbatik.")]
    [McpServerTool(
        Title = "Verbatik File-to-Speech",
        Name = "verbatik_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Verbatik_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("Voice ID to use. Default: matthew-en-us.")]
        string voice_id = "matthew-en-us",
        [Description("Whether Verbatik should store audio in S3 and return audio_url. Default: false.")]
        bool store_audio = false,
        [Description("Content type of the input body. Allowed: text/plain, application/ssml+xml. Default: text/plain.")]
        string content_type = "text/plain",
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
                new VerbatikFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = string.IsNullOrWhiteSpace(voice_id) ? "matthew-en-us" : voice_id.Trim(),
                    StoreAudio = store_audio,
                    ContentType = NormalizeContentType(content_type),
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
                typed.StoreAudio,
                typed.ContentType,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string input,
        string voiceId,
        bool storeAudio,
        string contentType,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateInput(input, voiceId);

        var settings = serviceProvider.GetRequiredService<VerbatikSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var resolvedContentType = NormalizeContentType(contentType);

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, TtsUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Add("X-Voice-ID", voiceId);
        req.Headers.Add("X-Store-Audio", storeAudio ? "true" : "false");
        req.Content = new StringContent(input, Encoding.UTF8, resolvedContentType);

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Verbatik speech call failed ({(int)resp.StatusCode}): {body}");
        }

        byte[] audioBytes;
        string? mediaType = resp.Content.Headers.ContentType?.MediaType;

        if (storeAudio)
        {
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            var audioUrl = doc.RootElement.TryGetProperty("audio_url", out var audioUrlEl)
                && audioUrlEl.ValueKind == JsonValueKind.String
                ? audioUrlEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(audioUrl))
                throw new InvalidOperationException("Verbatik returned store_audio response without audio_url.");

            using var audioResp = await client.GetAsync(audioUrl, cancellationToken);
            var downloaded = await audioResp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!audioResp.IsSuccessStatusCode)
            {
                var body = Encoding.UTF8.GetString(downloaded);
                throw new InvalidOperationException($"Verbatik audio download failed ({(int)audioResp.StatusCode}): {body}");
            }

            audioBytes = downloaded;
            mediaType = audioResp.Content.Headers.ContentType?.MediaType;
        }
        else
        {
            audioBytes = bytes;
        }

        var ext = ResolveExtension(mediaType, "mp3");
        var uploadName = filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(audioBytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static void ValidateInput(string input, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ValidationException("input is required.");

        if (input.Length > 10000)
            throw new ValidationException("input exceeds maximum length of 10,000 characters.");

        if (string.IsNullOrWhiteSpace(voiceId))
            throw new ValidationException("voice_id is required.");
    }

    private static string NormalizeContentType(string? contentType)
    {
        var value = (contentType ?? "text/plain").Trim().ToLowerInvariant();
        return value is "text/plain" or "application/ssml+xml" ? value : "text/plain";
    }

    private static string ResolveExtension(string? mimeType, string fallback)
    {
        var mediaType = mimeType?.Trim().ToLowerInvariant();
        return mediaType switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/flac" => "flac",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            _ => fallback
        };
    }

    [Description("Please fill in the Verbatik text-to-speech request.")]
    public sealed class VerbatikTextToSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text or SSML to synthesize.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Verbatik voice ID. Default: matthew-en-us.")]
        public string VoiceId { get; set; } = "matthew-en-us";

        [JsonPropertyName("store_audio")]
        [Description("Store generated audio and return URL before MCP upload.")]
        public bool StoreAudio { get; set; } = false;

        [JsonPropertyName("content_type")]
        [Required]
        [Description("Body content type: text/plain or application/ssml+xml.")]
        public string ContentType { get; set; } = "text/plain";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Verbatik file-to-speech request.")]
    public sealed class VerbatikFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Verbatik voice ID. Default: matthew-en-us.")]
        public string VoiceId { get; set; } = "matthew-en-us";

        [JsonPropertyName("store_audio")]
        [Description("Store generated audio and return URL before MCP upload.")]
        public bool StoreAudio { get; set; } = false;

        [JsonPropertyName("content_type")]
        [Required]
        [Description("Body content type: text/plain or application/ssml+xml.")]
        public string ContentType { get; set; } = "text/plain";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

