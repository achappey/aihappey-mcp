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

namespace MCPhappey.Tools.Speechify;

public static class SpeechifyAudio
{
    private const string SpeechUrl = "https://api.speechify.ai/v1/audio/speech";

    [Description("Generate speech audio from raw text using Speechify and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Speechify Text-to-Speech",
        Name = "speechify_audio_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SpeechifyAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string input,
        [Description("Speechify voice ID.")] string voice_id,
        [Description("Output audio format: wav, mp3, ogg, aac, pcm. Default: mp3.")] string audio_format = "mp3",
        [Description("Speechify model. Default: simba-english.")] string model = "simba-english",
        [Description("Language code like en-US, nl-NL.")] string? language = null,
        [Description("Enable loudness normalization (-14 LUFS target).")]
        bool loudness_normalization = false,
        [Description("Enable text normalization for numbers/dates.")]
        bool text_normalization = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SpeechifyTtsRequest
                {
                    Input = input,
                    VoiceId = voice_id,
                    AudioFormat = NormalizeAudioFormat(audio_format),
                    Model = model,
                    Language = language,
                    LoudnessNormalization = loudness_normalization,
                    TextNormalization = text_normalization,
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
                typed.AudioFormat,
                typed.Model,
                typed.Language,
                typed.LoudnessNormalization,
                typed.TextNormalization,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Speechify.")]
    [McpServerTool(
        Title = "Speechify File-to-Speech",
        Name = "speechify_audio_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SpeechifyAudio_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Speechify voice ID.")] string voice_id,
        [Description("Output audio format: wav, mp3, ogg, aac, pcm. Default: mp3.")] string audio_format = "mp3",
        [Description("Speechify model. Default: simba-english.")] string model = "simba-english",
        [Description("Language code like en-US, nl-NL.")] string? language = null,
        [Description("Enable loudness normalization (-14 LUFS target).")]
        bool loudness_normalization = false,
        [Description("Enable text normalization for numbers/dates.")]
        bool text_normalization = true,
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
                new SpeechifyFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = voice_id,
                    AudioFormat = NormalizeAudioFormat(audio_format),
                    Model = model,
                    Language = language,
                    LoudnessNormalization = loudness_normalization,
                    TextNormalization = text_normalization,
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
                typed.AudioFormat,
                typed.Model,
                typed.Language,
                typed.LoudnessNormalization,
                typed.TextNormalization,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string input,
        string voiceId,
        string audioFormat,
        string model,
        string? language,
        bool loudnessNormalization,
        bool textNormalization,
        string filename,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<SpeechifySettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var payload = new
        {
            input,
            voice_id = voiceId,
            audio_format = NormalizeAudioFormat(audioFormat),
            model,
            language,
            options = new
            {
                loudness_normalization = loudnessNormalization,
                text_normalization = textNormalization
            }
        };

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, SpeechUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("audio_data", out var audioDataEl)
            || audioDataEl.ValueKind != JsonValueKind.String)
            throw new Exception("Speechify response did not include 'audio_data'.");

        var base64 = audioDataEl.GetString();
        if (string.IsNullOrWhiteSpace(base64))
            throw new Exception("Speechify returned empty audio_data.");

        var bytes = Convert.FromBase64String(base64);
        var ext = ExtractAudioFormat(doc.RootElement) ?? NormalizeAudioFormat(audioFormat);
        var uploadName = $"{filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static string NormalizeAudioFormat(string? audioFormat)
    {
        var value = (audioFormat ?? "mp3").Trim().ToLowerInvariant();
        return value is "wav" or "mp3" or "ogg" or "aac" or "pcm" ? value : "mp3";
    }

    private static string? ExtractAudioFormat(JsonElement root)
    {
        if (!root.TryGetProperty("audio_format", out var format))
            return null;

        if (format.ValueKind == JsonValueKind.String)
            return NormalizeAudioFormat(format.GetString());

        return null;
    }

    [Description("Please fill in the Speechify text-to-speech request.")]
    public sealed class SpeechifyTtsRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text or SSML to synthesize.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Speechify voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("audio_format")]
        [Required]
        [Description("Audio format: wav, mp3, ogg, aac, pcm.")]
        public string AudioFormat { get; set; } = "mp3";

        [JsonPropertyName("model")]
        [Required]
        [Description("Speechify model. Recommended: simba-english or simba-multilingual.")]
        public string Model { get; set; } = "simba-english";

        [JsonPropertyName("language")]
        [Description("Optional input language (e.g. en-US, nl-NL).")]
        public string? Language { get; set; }

        [JsonPropertyName("loudness_normalization")]
        [Description("Normalize output loudness to standard target.")]
        public bool LoudnessNormalization { get; set; } = false;

        [JsonPropertyName("text_normalization")]
        [Description("Normalize numbers/dates to spoken words.")]
        public bool TextNormalization { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Speechify file-to-speech request.")]
    public sealed class SpeechifyFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Speechify voice ID.")]
        public string VoiceId { get; set; } = default!;

        [JsonPropertyName("audio_format")]
        [Required]
        [Description("Audio format: wav, mp3, ogg, aac, pcm.")]
        public string AudioFormat { get; set; } = "mp3";

        [JsonPropertyName("model")]
        [Required]
        [Description("Speechify model. Recommended: simba-english or simba-multilingual.")]
        public string Model { get; set; } = "simba-english";

        [JsonPropertyName("language")]
        [Description("Optional input language (e.g. en-US, nl-NL).")]
        public string? Language { get; set; }

        [JsonPropertyName("loudness_normalization")]
        [Description("Normalize output loudness to standard target.")]
        public bool LoudnessNormalization { get; set; } = false;

        [JsonPropertyName("text_normalization")]
        [Description("Normalize numbers/dates to spoken words.")]
        public bool TextNormalization { get; set; } = true;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

