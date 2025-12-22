using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Tools.StabilityAI;
using MCPhappey.Core.Services;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Mistral.Audio;

public static partial class AudioPlugin
{
    private const string MISTRAL_TRANSCRIBE_URL = "https://api.mistral.ai/v1/audio/transcriptions";

    [Description("Transcribe speech to text using Mistral’s Voxtral models.")]
    [McpServerTool(
        Title = "Mistral Audio Transcription",
        Name = "mistral_audio_transcribe",
        IconSource = MistralConstants.ICON_SOURCE,
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MistralAudio_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the audio file (.mp3, .wav, .m4a, .webm, .flac) to transcribe.")] string audioUrl,
        [Description("Optional text prompt to improve transcription quality.")] string? prompt = null,
        [Description("Language code (e.g. en, nl, fr). Use 'auto' for auto-detect.")] string? language = "auto",
        [Description("Sampling temperature (0–1). Default: 0.")] double temperature = 0,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(audioUrl);

            var settings = serviceProvider.GetRequiredService<MistralSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // 1) Download audio from SharePoint/OneDrive/HTTP
            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
            var audio = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio.");

            // 2) Elicit/confirm parameters
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new MistralAudioTranscription
                {
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Model = "voxtral-mini-latest",
                    Language = language ?? "auto",
                    Prompt = prompt,
                    Temperature = temperature,
                },
                cancellationToken);

            // 3) Build multipart form-data; prefer direct file upload
            using var form = new MultipartFormDataContent();

            var fileContent = new StreamContent(audio.Contents.ToStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(audio.MimeType ?? "audio/mpeg");
            form.Add(fileContent, "file", audio.Filename ?? "input.mp3");

            form.Add("model".NamedField(typed.Model));
            form.Add("stream".NamedField("false"));
            form.Add("temperature".NamedField(typed.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (!string.IsNullOrWhiteSpace(typed.Language))
                form.Add("language".NamedField(typed.Language));

            if (!string.IsNullOrWhiteSpace(typed.Prompt))
                form.Add("prompt".NamedField(typed.Prompt));

            // 4) Send request
            using var client = clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var resp = await client.PostAsync(MISTRAL_TRANSCRIBE_URL, form, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            // 5) Extract text
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : json;

            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("No transcription text found in response.");

            // 6) Upload .txt artifact
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(text!),
                cancellationToken);

            // 7) Return structured content
            return new CallToolResult
            {
                Content =
                [
                    text!.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    [Description("Please fill in the Mistral audio transcription request.")]
    public class MistralAudioTranscription
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Transcription model. Default: voxtral-mini-latest.")]
        public string Model { get; set; } = "voxtral-mini-latest";

        [JsonPropertyName("language")]
        [Description("Language code (ISO 639-1). Use 'auto' for detection.")]
        public string? Language { get; set; } = "auto";

        [JsonPropertyName("prompt")]
        [Description("Optional text bias for decoding.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Required]
        [Description("Sampling temperature between 0.0 and 1.0. Default: 0.")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}
