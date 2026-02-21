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

namespace MCPhappey.Tools.Together.Audio;

public static class TogetherAudio
{
    private const string BASE_URL = "https://api.together.xyz/v1/audio/speech";

    [Description("Generate lifelike speech from text using Together AI voice models.")]
    [McpServerTool(
        Title = "Text-to-Speech",
        Name = "together_audio_text_to_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> TogetherAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to be spoken.")] string input,
        [Description("Voice style, e.g. 'laidback woman' or 'storyteller lady'.")] string voice = "storyteller lady",
        [Description("Text-to-speech model. canopylabs/orpheus-3b-0.1-ft, hexgrad/Kokoro-82M, cartesia/sonic-2 or cartesia/sonic")] string? model = "cartesia/sonic",
        [Description("Audio format (mp3, wav, raw). Default: wav.")] string responseFormat = "wav",
        [Description("Language code, e.g. en, de, fr, nl, zh. Default: en.")] string language = "en",
        [Description("Sample rate in Hz. Default: 44100.")] int sampleRate = 44100,
        [Description("Output filename (without extension).")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            // 1️⃣ Elicit or confirm model input
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new TogetherAudioTextToSpeech
                {
                    Model = model ?? "cartesia/sonic",
                    Input = input,
                    Voice = voice,
                    ResponseFormat = responseFormat,
                    Language = language,
                    SampleRate = sampleRate,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            // 2️⃣ Prepare JSON payload
            var payload = new
            {
                model = typed.Model,
                input = typed.Input,
                voice = typed.Voice,
                response_format = typed.ResponseFormat,
                language = typed.Language,
                response_encoding = "pcm_f32le",
                sample_rate = typed.SampleRate,
                stream = false
            };

            using var client = serviceProvider.CreateTogetherClient("audio/*");
            using var request = new HttpRequestMessage(HttpMethod.Post, BASE_URL);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            // 3️⃣ Execute request
            using var resp = await client.SendAsync(request, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var errText = Encoding.UTF8.GetString(bytesOut);
                throw new Exception($"{resp.StatusCode}: {errText}");
            }

            // 4️⃣ Upload result to Graph (so it’s stored & viewable)
            var fileExt = typed.ResponseFormat switch
            {
                "mp3" => "mp3",
                "raw" => "raw",
                _ => "wav"
            };

            var graphItem = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{fileExt}",
                BinaryData.FromBytes(bytesOut),
                cancellationToken);

            if (graphItem == null)
                throw new Exception("Audio upload failed");

            // 5️⃣ Return structured result (Graph link + playable audio block)
            return new CallToolResult
            {
                Content = [
                    graphItem,
                    new AudioContentBlock
                    {
                        Data = bytesOut,
                        MimeType = fileExt == "mp3" ? "audio/mpeg" : "audio/wav"
                    }
                ]
            };
        });

    [Description("Please fill in the Together AI text-to-speech generation request.")]
    public class TogetherAudioTextToSpeech
    {
        [Required]
        [JsonPropertyName("model")]
        [Description("The Together model to use. canopylabs/orpheus-3b-0.1-ft, hexgrad/Kokoro-82M, cartesia/sonic-2 or cartesia/sonic")]
        public string Model { get; set; } = "cartesia/sonic";

        [Required]
        [JsonPropertyName("input")]
        [Description("The text input to synthesize into speech.")]
        public string Input { get; set; } = default!;

        [Required]
        [JsonPropertyName("voice")]
        [Description("Voice preset name, e.g. 'laidback woman' or 'friendly sidekick'.")]
        public string Voice { get; set; } = "storyteller lady";

        [JsonPropertyName("response_format")]
        [Description("Audio format (mp3, wav, raw). Default: wav.")]
        public string ResponseFormat { get; set; } = "wav";

        [JsonPropertyName("language")]
        [Description("Language of input text. Default: en.")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("response_encoding")]
        [Description("Audio encoding. Default: pcm_f32le.")]
        public string ResponseEncoding { get; set; } = "pcm_f32le";

        [JsonPropertyName("sample_rate")]
        [Description("Output sampling rate (Hz). Default: 44100.")]
        public int SampleRate { get; set; } = 44100;

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    private const string TRANSCRIBE_URL = "https://api.together.xyz/v1/audio/transcriptions";

    [Description("Transcribe speech to text using Together AI STT models.")]
    [McpServerTool(
        Title = "Together Audio Transcription",
        Name = "together_audio_transcribe_audio",
        Destructive = false)]
    public static async Task<CallToolResult?> TogetherAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the audio file (.mp3, .wav, .m4a, .webm, .flac) to transcribe.")] string audioUrl,
        [Description("Optional text prompt to improve transcription quality.")] string? prompt = null,
        [Description("Speech-to-text model. openai/whisper-large-v3 or mistralai/Voxtral-Mini-3B-2507")] string? model = "openai/whisper-large-v3",
        [Description("Language code (e.g. en, nl, fr). Use 'auto' for auto-detect. Default: en.")] string? language = "en",
        [Description("Sampling temperature (0–1). Default: 0.")] double temperature = 0,
        [Description("Enable speaker diarization.")] bool diarize = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            // 1️⃣ Download audio (SharePoint, OneDrive, or HTTP)
            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
            var audio = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio.");

            // 2️⃣ Confirm parameters via elicitation
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new TogetherAudioTranscription
                {
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Model = model ?? "openai/whisper-large-v3",
                    Language = language ?? "en",
                    Prompt = prompt,
                    Diarize = diarize,
                    Temperature = temperature,
                },
                cancellationToken);

            // 3️⃣ Build multipart form
            using var form = new MultipartFormDataContent
            {
                {
                    new StreamContent(audio.Contents.ToStream())
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue(audio.MimeType) }
                    },
                    "file",
                    audio.Filename ?? "input.mp3"
                },
                "model".NamedField(typed.Model),
                "diarize".NamedField(typed.Diarize.ToString().ToLower()),
                "language".NamedField(typed.Language),
                "response_format".NamedField("json"),
                "temperature".NamedField(typed.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };

            if (!string.IsNullOrWhiteSpace(typed.Prompt))
                form.Add("prompt".NamedField(typed.Prompt));

            // 4️⃣ Send request
            using var client = serviceProvider.CreateTogetherClient();

            using var resp = await client.PostAsync(TRANSCRIBE_URL, form, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            // 5️⃣ Extract transcribed text
            var result = JsonDocument.Parse(json);
            var text = result.RootElement.TryGetProperty("text", out var t) ? t.GetString() : json;

            if (string.IsNullOrWhiteSpace(text))
                throw new Exception("No transcription text found in response.");

            // 6️⃣ Upload .txt to Graph
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(text!),
                cancellationToken);

            // 7️⃣ Return as structured content
            return new CallToolResult
            {
                Content = [
                    text.ToTextContentBlock(),
                    uploaded!,
                ]
            };
        });


    [Description("Please fill in the Together AI audio transcription request.")]
    public class TogetherAudioTranscription
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Transcription model. openai/whisper-large-v3 or mistralai/Voxtral-Mini-3B-2507.")]
        public string Model { get; set; } = "openai/whisper-large-v3";

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code (ISO 639-1). Use 'auto' for detection.")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("prompt")]
        [Description("Optional text bias for decoding.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("temperature")]
        [Range(0, 1)]
        [Required]
        [Description("Sampling temperature between 0.0 and 1.0. Default: 0.")]
        public double Temperature { get; set; } = 0;

        [JsonPropertyName("diarize")]
        [Required]
        [Description("Enable speaker diarization.")]
        [DefaultValue(true)]
        public bool Diarize { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}


