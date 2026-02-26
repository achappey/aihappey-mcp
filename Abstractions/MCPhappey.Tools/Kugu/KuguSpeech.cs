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

namespace MCPhappey.Tools.Kugu;

public static class KuguSpeech
{
    private const string TtsUrl = "https://api.kugu.ai/api/v1/tts";

    [Description("Generate speech audio from raw text using Kugu and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Kugu Text-to-Speech",
        Name = "kugu_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Kugu_Speech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Kugu model. Default: google-chirp3-hd.")]
        string model = "google-chirp3-hd",
        [Description("Kugu voice ID. Default: en-US-Chirp3-HD-Charon.")]
        string voice = "en-US-Chirp3-HD-Charon",
        [Description("Language code (e.g., en, nl). Default: en.")]
        string language = "en",
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new KuguSpeechTextToSpeechRequest
                {
                    Text = text,
                    Model = string.IsNullOrWhiteSpace(model) ? "google-chirp3-hd" : model.Trim(),
                    Voice = string.IsNullOrWhiteSpace(voice) ? "en-US-Chirp3-HD-Charon" : voice.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim(),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.Model,
                typed.Voice,
                typed.Language,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Kugu.")]
    [McpServerTool(
        Title = "Kugu File-to-Speech",
        Name = "kugu_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Kugu_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("Kugu model. Default: google-chirp3-hd.")]
        string model = "google-chirp3-hd",
        [Description("Kugu voice ID. Default: en-US-Chirp3-HD-Charon.")]
        string voice = "en-US-Chirp3-HD-Charon",
        [Description("Language code (e.g., en, nl). Default: en.")]
        string language = "en",
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
                new KuguSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Model = string.IsNullOrWhiteSpace(model) ? "google-chirp3-hd" : model.Trim(),
                    Voice = string.IsNullOrWhiteSpace(voice) ? "en-US-Chirp3-HD-Charon" : voice.Trim(),
                    Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim(),
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
                typed.Language,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string text,
        string model,
        string voice,
        string language,
        string filename,
        CancellationToken cancellationToken)
    {
        ValidateInput(text, model, voice, language);

        var settings = serviceProvider.GetRequiredService<KuguSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = clientFactory.CreateClient();

        var payload = new
        {
            model,
            text,
            voice,
            language
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, TtsUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Kugu speech call failed ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var audioUrl = ExtractAudioUrl(doc.RootElement);
        if (string.IsNullOrWhiteSpace(audioUrl))
            throw new InvalidOperationException("Kugu speech response did not include output.audio_url.");

        using var mediaResp = await client.GetAsync(audioUrl, cancellationToken);
        var bytes = await mediaResp.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!mediaResp.IsSuccessStatusCode)
        {
            var errorText = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Kugu speech download failed ({(int)mediaResp.StatusCode}): {errorText}");
        }

        var ext = GuessAudioExtension(audioUrl, mediaResp.Content.Headers.ContentType?.MediaType) ?? "mp3";
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

    private static void ValidateInput(string text, string model, string voice, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required.");

        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        if (string.IsNullOrWhiteSpace(voice))
            throw new ValidationException("voice is required.");

        if (string.IsNullOrWhiteSpace(language))
            throw new ValidationException("language is required.");
    }

    private static string? ExtractAudioUrl(JsonElement root)
    {
        if (root.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Object
            && output.TryGetProperty("audio_url", out var audioUrlEl)
            && audioUrlEl.ValueKind == JsonValueKind.String)
        {
            var url = audioUrlEl.GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        }

        if (root.TryGetProperty("audio_url", out var rootAudioUrlEl)
            && rootAudioUrlEl.ValueKind == JsonValueKind.String)
        {
            var url = rootAudioUrlEl.GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        }

        return null;
    }

    private static string? GuessAudioExtension(string? audioUrl, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(audioUrl))
        {
            if (audioUrl.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return "wav";
            if (audioUrl.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)) return "flac";
            if (audioUrl.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return "ogg";
            if (audioUrl.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return "mp3";
            if (audioUrl.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) return "m4a";
            if (audioUrl.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)) return "aac";
        }

        var mt = mediaType?.Trim().ToLowerInvariant();
        return mt switch
        {
            "audio/wav" or "audio/x-wav" => "wav",
            "audio/flac" => "flac",
            "audio/ogg" => "ogg",
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/mp4" => "m4a",
            "audio/aac" => "aac",
            _ => null
        };
    }

    [Description("Please fill in the Kugu text-to-speech request.")]
    public sealed class KuguSpeechTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Plain text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Kugu model ID. Default: google-chirp3-hd.")]
        public string Model { get; set; } = "google-chirp3-hd";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Kugu voice ID. Default: en-US-Chirp3-HD-Charon.")]
        public string Voice { get; set; } = "en-US-Chirp3-HD-Charon";

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code (e.g. en, nl).")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Kugu file-to-speech request.")]
    public sealed class KuguSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Kugu model ID. Default: google-chirp3-hd.")]
        public string Model { get; set; } = "google-chirp3-hd";

        [JsonPropertyName("voice")]
        [Required]
        [Description("Kugu voice ID. Default: en-US-Chirp3-HD-Charon.")]
        public string Voice { get; set; } = "en-US-Chirp3-HD-Charon";

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code (e.g. en, nl).")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

