using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
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

namespace MCPhappey.Tools.SiliconFlow.Audio;

public static class SiliconFlowAudio
{
    private const string SpeechPath = "audio/speech";
    private const string TranscriptionsPath = "audio/transcriptions";

    [Description("Generate speech audio from text using SiliconFlow and return both uploaded file link and inline audio.")]
    [McpServerTool(
        Title = "SiliconFlow Text-to-Speech",
        Name = "siliconflow_audio_create_speech",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SiliconFlowAudio_CreateSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The text to generate audio for.")] string input,
        [Description("SiliconFlow TTS model. Examples: fishaudio/fish-speech-1.5, FunAudioLLM/CosyVoice2-0.5B, IndexTeam/IndexTTS-2.")] string model = "fishaudio/fish-speech-1.5",
        [Description("Voice preset. Example: fishaudio/fish-speech-1.5:alex")] string voice = "fishaudio/fish-speech-1.5:alex",
        [Description("Output format: mp3, opus, wav, or pcm. Default: mp3.")] string responseFormat = "mp3",
        [Description("Optional sample rate in Hz. Use values supported by the selected output format.")] int? sampleRate = null,
        [Description("Enable streaming mode. Default: false.")] bool stream = false,
        [Description("Speech speed from 0.25 to 4.0. Default: 1.0.")] double speed = 1.0,
        [Description("Output gain from -10 to 10. Default: 0.")] double gain = 0,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(input);

            var settings = serviceProvider.GetRequiredService<SiliconFlowSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SiliconFlowCreateSpeechRequest
                {
                    Input = input,
                    Model = model,
                    Voice = voice,
                    ResponseFormat = responseFormat,
                    SampleRate = sampleRate,
                    Stream = stream,
                    Speed = speed,
                    Gain = gain,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            ValidateSpeechRequest(typed);

            var payload = new
            {
                model = typed.Model,
                input = typed.Input,
                voice = typed.Voice,
                response_format = typed.ResponseFormat,
                sample_rate = typed.SampleRate,
                stream = typed.Stream,
                speed = typed.Speed,
                gain = typed.Gain
            };

            using var client = clientFactory.CreateClient();
            client.BaseAddress = new Uri("https://api.siliconflow.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));

            using var req = new HttpRequestMessage(HttpMethod.Post, SpeechPath)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var error = Encoding.UTF8.GetString(bytes);
                throw new Exception($"{resp.StatusCode}: {error}");
            }

            var ext = typed.ResponseFormat.ToLowerInvariant();
            var mimeType = ext switch
            {
                "mp3" => "audio/mpeg",
                "opus" => "audio/opus",
                "pcm" => "application/octet-stream",
                _ => "audio/wav"
            };

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{ext}",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            if (uploaded == null)
                throw new Exception("Audio upload failed.");

            return new CallToolResult
            {
                Content =
                [
                    uploaded,
                    new AudioContentBlock
                    {
                        Data = Convert.ToBase64String(bytes),
                        MimeType = mimeType
                    }
                ]
            };
        });

    [Description("Transcribe audio using SiliconFlow. Supports SharePoint, OneDrive, and HTTP file URLs.")]
    [McpServerTool(
        Title = "SiliconFlow Audio Transcription",
        Name = "siliconflow_audio_transcribe_audio",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SiliconFlowAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the audio file to transcribe (.mp3, .wav, .m4a, .webm, .flac). Supports SharePoint and OneDrive URLs.")] string fileUrl,
        [Description("Transcription model. FunAudioLLM/SenseVoiceSmall or TeleAI/TeleSpeechASR.")] string model = "FunAudioLLM/SenseVoiceSmall",
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var settings = serviceProvider.GetRequiredService<SiliconFlowSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var audio = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio content from fileUrl.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SiliconFlowTranscriptionRequest
                {
                    Model = model,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            ValidateTranscriptionModel(typed.Model);

            using var form = new MultipartFormDataContent
            {
                {
                    new StreamContent(audio.Contents.ToStream())
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue(audio.MimeType ?? "audio/mpeg") }
                    },
                    "file",
                    audio.Filename ?? "input.mp3"
                },
                "model".NamedField(typed.Model)
            };

            using var client = clientFactory.CreateClient();
            client.BaseAddress = new Uri("https://api.siliconflow.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var resp = await client.PostAsync(TranscriptionsPath, form, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("text", out var textProperty)
                ? textProperty.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("No transcription text found in SiliconFlow response.");

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(text),
                cancellationToken);

            if (uploaded == null)
                throw new Exception("Transcription upload failed.");

            return new CallToolResult
            {
                Content =
                [
                    text.ToTextContentBlock(),
                    uploaded
                ]
            };
        });

    [Description("Please fill in the SiliconFlow text-to-speech request details.")]
    public class SiliconFlowCreateSpeechRequest
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("SiliconFlow speech model.")]
        public string Model { get; set; } = "fishaudio/fish-speech-1.5";

        [JsonPropertyName("input")]
        [Required]
        [Description("Input text to synthesize.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice preset identifier for the chosen model.")]
        public string Voice { get; set; } = "fishaudio/fish-speech-1.5:alex";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Output format: mp3, opus, wav, or pcm.")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonPropertyName("sample_rate")]
        [Description("Optional sample rate in Hz.")]
        public int? SampleRate { get; set; }

        [JsonPropertyName("stream")]
        [Required]
        [Description("Use streaming output.")]
        public bool Stream { get; set; }

        [JsonPropertyName("speed")]
        [Range(0.25, 4.0)]
        [Required]
        [Description("Speech speed between 0.25 and 4.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("gain")]
        [Range(-10, 10)]
        [Required]
        [Description("Output gain between -10 and 10.")]
        public double Gain { get; set; } = 0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the SiliconFlow transcription request details.")]
    public class SiliconFlowTranscriptionRequest
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Transcription model. FunAudioLLM/SenseVoiceSmall or TeleAI/TeleSpeechASR.")]
        public string Model { get; set; } = "FunAudioLLM/SenseVoiceSmall";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    private static void ValidateSpeechRequest(SiliconFlowCreateSpeechRequest input)
    {
        var format = input.ResponseFormat?.Trim().ToLowerInvariant();
        if (format is not ("mp3" or "opus" or "wav" or "pcm"))
            throw new Exception("Invalid responseFormat. Supported values: mp3, opus, wav, pcm.");

        if (input.SampleRate.HasValue)
        {
            var allowed = new[] { 8000, 16000, 24000, 32000, 44100, 48000 };
            if (!allowed.Contains(input.SampleRate.Value))
                throw new Exception("Invalid sampleRate. Supported values: 8000, 16000, 24000, 32000, 44100, 48000.");
        }

        if (input.Speed is < 0.25 or > 4.0)
            throw new Exception("Invalid speed. Supported range: 0.25 to 4.0.");

        if (input.Gain is < -10 or > 10)
            throw new Exception("Invalid gain. Supported range: -10 to 10.");
    }

    private static void ValidateTranscriptionModel(string model)
    {
        if (model is "FunAudioLLM/SenseVoiceSmall" or "TeleAI/TeleSpeechASR")
            return;

        throw new Exception("Invalid model. Supported values: FunAudioLLM/SenseVoiceSmall, TeleAI/TeleSpeechASR.");
    }
}

