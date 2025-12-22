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

namespace MCPhappey.Tools.ElevenLabs;

public static class ElevenLabsAudio
{
    private const string BASE_URL_TTS = "https://api.elevenlabs.io/v1/text-to-speech";
    private const string BASE_URL_STT = "https://api.elevenlabs.io/v1/speech-to-text";

    [Description("Generate speech audio from input text using ElevenLabs voice models.")]
    [McpServerTool(
        Title = "ElevenLabs Text-to-Speech",
        Name = "elevenlabs_audio_text_to_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> ElevenLabsAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to convert into speech.")] string input,
        [Description("ElevenLabs voice ID.")] string voice_id,
        [Description("Model name. Default: eleven_multilingual_v2")] string model_id = "eleven_multilingual_v2",
        [Description("Output filename (without extension).")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<ElevenLabsSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new ElevenLabsTTSRequest
                {
                    Input = input,
                    Voice_Id = voice_id,
                    Model_Id = model_id,
                    //  Output_Format = output_format,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            var payload = new
            {
                text = typed.Input,
                model_id = typed.Model_Id
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BASE_URL_TTS}/{typed.Voice_Id}?output_format=mp3_44100_128");
            req.Headers.Authorization = new AuthenticationHeaderValue("xi-api-key", settings.ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {Encoding.UTF8.GetString(bytesOut)}");

            //var ext = typed.Output_Format.Contains("mp3") ? "mp3" : "wav";
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{"mp3"}",
                BinaryData.FromBytes(bytesOut),
                cancellationToken);

            return new CallToolResult
            {
                Content = [
                    uploaded!,
                    new AudioContentBlock {
                        Data = Convert.ToBase64String(bytesOut),
                        MimeType =  "audio/mpeg"
                    }
                ]
            };
        });

    [Description("Please fill in the ElevenLabs text-to-speech request.")]
    public class ElevenLabsTTSRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("The text to convert into natural speech audio.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("The ElevenLabs voice ID to use for generation.")]
        public string Voice_Id { get; set; } = default!;

        [JsonPropertyName("model_id")]
        [Required]
        [Description("The ElevenLabs model to use. Default: eleven_multilingual_v2.")]
        public string Model_Id { get; set; } = "eleven_multilingual_v2";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension. The tool will attach the appropriate extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Transcribe audio into text using ElevenLabs speech recognition.")]
    [McpServerTool(
        Title = "ElevenLabs Speech-to-Text",
        Name = "elevenlabs_audio_transcribe_audio",
        Destructive = false)]
    public static async Task<CallToolResult?> ElevenLabsAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio file URL")] string audioUrl,
        [Description("Optional language code. Auto if null.")] string? language_code = null,
        [Description("File name of resulting transcript.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<ElevenLabsSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, audioUrl, cancellationToken);
            var audio = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio content.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new ElevenLabsSTTRequest
                {
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Model_Id = "scribe_v1",
                    Language_Code = language_code
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var form = new MultipartFormDataContent
            {
                { new StreamContent(audio.Contents.ToStream()), "file", audio.Filename ?? "audio.mp3" },
                "model_id".NamedField(typed.Model_Id)
            };

            if (!string.IsNullOrWhiteSpace(typed.Language_Code))
                form.Add("language_code".NamedField(typed.Language_Code));

            using var client = clientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("xi-api-key", settings.ApiKey);

            using var resp = await client.PostAsync(BASE_URL_STT, form, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("text", out var t)
                ? t.GetString()
                : json;

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.txt",
                BinaryData.FromString(text!),
                cancellationToken);

            return new CallToolResult
            {
                Content = [
                    text!.ToTextContentBlock(),
                    uploaded!
                ]
            };
        });

    [Description("Please fill in the ElevenLabs speech-to-text request.")]
    public class ElevenLabsSTTRequest
    {
        [JsonPropertyName("model_id")]
        [Required]
        [Description("The model used for transcription. Default: scribe_v1.")]
        public string Model_Id { get; set; } = "scribe_v1";

        [JsonPropertyName("language_code")]
        [Description("Optional language hint (ISO-639-1). Leave blank if unknown. Auto-detect will be used.")]
        public string? Language_Code { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension. A .txt file will be created.")]
        public string Filename { get; set; } = default!;
    }
}

public class ElevenLabsSettings
{
    public string ApiKey { get; set; } = default!;
}
