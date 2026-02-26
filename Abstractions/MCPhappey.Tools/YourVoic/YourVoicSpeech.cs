using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.StabilityAI;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.YourVoic;

public static class YourVoicSpeech
{
    private const string TtsGeneratePath = "tts/generate";

    [Description("Generate speech from text using YourVoic and return uploaded audio plus playable inline audio.")]
    [McpServerTool(
        Title = "YourVoic Text-to-Speech",
        Name = "yourvoic_speech_generate",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> YourVoic_Speech_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The text to convert to speech (max 5000 chars).")]
        string text,
        [Description("Voice name. Default: Peter.")]
        string voice = "Peter",
        [Description("Language code, e.g. en-US, hi, ja-JP. Default: en-US.")]
        string language = "en-US",
        [Description("Model id: aura-lite, aura-prime, aura-max, rapid-flash, rapid-max. Default: aura-prime.")]
        string model = "aura-prime",
        [Description("Playback speed from 0.5 to 2.0. Default: 1.0.")]
        double speed = 1.0,
        [Description("Voice pitch from 0.5 to 2.0. Default: 1.0.")]
        double pitch = 1.0,
        [Description("Output format: mp3 or wav. Default: mp3.")]
        string format = "mp3",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new YourVoicSpeechGenerateRequest
                {
                    Text = text,
                    Voice = voice,
                    Language = language,
                    Model = NormalizeModel(model),
                    Speed = ClampRange(speed, 0.5, 2.0),
                    Pitch = ClampRange(pitch, 0.5, 2.0),
                    Format = NormalizeFormat(format),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            using var client = serviceProvider.CreateYourVoicClient("audio/*");
            using var req = new HttpRequestMessage(HttpMethod.Post, TtsGeneratePath)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        text = typed.Text,
                        voice = typed.Voice,
                        language = typed.Language,
                        model = NormalizeModel(typed.Model),
                        speed = ClampRange(typed.Speed, 0.5, 2.0),
                        pitch = ClampRange(typed.Pitch, 0.5, 2.0),
                        format = NormalizeFormat(typed.Format)
                    }),
                    Encoding.UTF8,
                    MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var err = Encoding.UTF8.GetString(bytesOut);
                throw new InvalidOperationException($"YourVoic TTS failed ({(int)resp.StatusCode}): {err}");
            }

            var ext = NormalizeFormat(typed.Format);
            var mimeType = ext == "wav" ? "audio/wav" : MimeTypes.AudioMP3;

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{ext}",
                BinaryData.FromBytes(bytesOut),
                cancellationToken);

            if (uploaded == null)
                throw new InvalidOperationException("YourVoic speech upload failed.");

            return new CallToolResult
            {
                Content =
                [
                    uploaded,
                    new AudioContentBlock
                    {
                        Data = bytesOut,
                        MimeType = mimeType
                    }
                ]
            };
        });

    [Description("Please fill in the YourVoic text-to-speech request.")]
    public sealed class YourVoicSpeechGenerateRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("The text to convert to speech.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("voice")]
        [Required]
        [Description("Voice name.")]
        public string Voice { get; set; } = "Peter";

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code, e.g. en-US.")]
        public string Language { get; set; } = "en-US";

        [JsonPropertyName("model")]
        [Required]
        [Description("Model id: aura-lite, aura-prime, aura-max, rapid-flash, rapid-max.")]
        public string Model { get; set; } = "aura-prime";

        [JsonPropertyName("speed")]
        [Range(0.5, 2.0)]
        [Description("Playback speed from 0.5 to 2.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("pitch")]
        [Range(0.5, 2.0)]
        [Description("Voice pitch from 0.5 to 2.0.")]
        public double Pitch { get; set; } = 1.0;

        [JsonPropertyName("format")]
        [Required]
        [Description("Output format: mp3 or wav.")]
        public string Format { get; set; } = "mp3";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    private static double ClampRange(double value, double min, double max)
        => Math.Clamp(value, min, max);

    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? "mp3").Trim().ToLowerInvariant();
        return value is "mp3" or "wav" ? value : "mp3";
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "aura-prime").Trim();
        return value is "aura-lite" or "aura-prime" or "aura-max" or "rapid-flash" or "rapid-max"
            ? value
            : "aura-prime";
    }
}

