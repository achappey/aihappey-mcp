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

namespace MCPhappey.Tools.Telnyx;

public static class TelnyxSpeechToTextPlugin
{
    private const string TranscribeUrl = "https://api.telnyx.com/v2/ai/audio/transcriptions";

    [Description("Transcribe speech to text using Telnyx AI Inference.")]
    [McpServerTool(
        Title = "Telnyx Speech-to-Text",
        Name = "telnyx_audio_transcribe",
        Destructive = false)]
    public static async Task<CallToolResult?> TelnyxAudio_Transcribe(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the audio file (.flac, .mp3, .mp4, .mpeg, .mpga, .m4a, .ogg, .wav, .webm) to transcribe.")] string fileUrl,
        [Description("Model id to use. Default: distil-whisper/distil-large-v2.")] string model = "distil-whisper/distil-large-v2",
        [Description("Response format: json or verbose_json.")] string responseFormat = "json",
        [Description("Include segment timestamps (requires verbose_json).")]
        bool includeSegmentTimestamps = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var settings = serviceProvider.GetRequiredService<TelnyxSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var audio = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio content.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new TelnyxSpeechToTextInput
                {
                    FileUrl = fileUrl,
                    Model = model,
                    ResponseFormat = responseFormat,
                    IncludeSegmentTimestamps = includeSegmentTimestamps,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var form = new MultipartFormDataContent();

            var fileContent = new StreamContent(audio.Contents.ToStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(audio.MimeType ?? "audio/mpeg");
            form.Add(fileContent, "file", audio.Filename ?? "input.mp3");
            form.Add("model".NamedField(typed.Model));
            form.Add("response_format".NamedField(typed.ResponseFormat));

            if (typed.IncludeSegmentTimestamps &&
                typed.ResponseFormat.Equals("verbose_json", StringComparison.OrdinalIgnoreCase))
            {
                form.Add("segment".NamedField("timestamp_granularities[]"));
            }

            using var client = clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var response = await client.PostAsync(TranscribeUrl, form, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"{response.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var transcript = doc.RootElement.TryGetProperty("text", out var t)
                ? t.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(transcript))
                throw new Exception("No transcription text found in Telnyx response.");

            var uploadedTxt = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(transcript),
                cancellationToken);

            var uploadedJson = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.json",
                BinaryData.FromString(json),
                cancellationToken);

            return new CallToolResult
            {
                Content =
                [
                    transcript.ToTextContentBlock(),
                    uploadedTxt!,
                    uploadedJson!
                ]
            };
        });

    [Description("Please fill in the Telnyx speech-to-text request.")]
    public class TelnyxSpeechToTextInput
    {
        [JsonPropertyName("file_url")]
        [Required]
        [Description("Audio file URL used as source input.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model id to use for transcription.")]
        public string Model { get; set; } = "distil-whisper/distil-large-v2";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Transcript response format: json or verbose_json.")]
        public string ResponseFormat { get; set; } = "json";

        [JsonPropertyName("include_segment_timestamps")]
        [Description("If true, requests segment timestamps when response_format is verbose_json.")]
        public bool IncludeSegmentTimestamps { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

