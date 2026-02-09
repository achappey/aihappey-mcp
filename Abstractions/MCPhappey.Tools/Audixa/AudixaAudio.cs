using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Audixa;

public static class AudixaAudio
{
    private const string TTS_URL = "https://api.audixa.ai/v2/tts";
    private const string STATUS_URL = "https://api.audixa.ai/v2/status";

    [Description("Generate speech audio from text using Audixa text-to-speech.")]
    [McpServerTool(
        Title = "Audixa Text-to-Speech",
        Name = "audixa_audio_text_to_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> AudixaAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to convert to speech (max 5000 chars).")]
        [MaxLength(5000)]
        string text,
        [Description("Audixa voice ID to use (from /voices).")]
        string voice,
        [Description("Model tier: base or advance.")]
        string model = "base",
        [Description("Playback speed multiplier (0.5 - 2.0).")]
        double speed = 1.0,
        [Description("Emotion tone (advance model): neutral, happy, sad, angry, surprised.")]
        string emotion = "neutral",
        [Description("Creativity control (advance model, 0.7 - 1.0).")]
        double temperature = 0.9,
        [Description("Plausibility filter (advance model, 0.7 - 0.98).")]
        double top_p = 0.9,
        [Description("Output filename (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<AudixaSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // 1️⃣ Elicit or confirm settings before generation
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new AudixaTTSRequest
                {
                    Text = text,
                    Voice = voice,
                    Model = model,
                    Speed = speed,
                    Emotion = emotion,
                    Temperature = temperature,
                    TopP = top_p,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var client = clientFactory.CreateClient();

            // 2️⃣ Submit generation job
            var ttsPayload = new
            {
                text = typed.Text,
                voice = typed.Voice,
                model = typed.Model,
                speed = typed.Speed,
                emotion = typed.Emotion,
                temperature = typed.Temperature,
                top_p = typed.TopP
            };

            using var ttsRequest = new HttpRequestMessage(HttpMethod.Post, TTS_URL);
            ttsRequest.Headers.Add("x-api-key", settings.ApiKey);
            ttsRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            ttsRequest.Content = new StringContent(JsonSerializer.Serialize(ttsPayload), Encoding.UTF8, MimeTypes.Json);

            using var ttsResponse = await client.SendAsync(ttsRequest, cancellationToken);
            var ttsJson = await ttsResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!ttsResponse.IsSuccessStatusCode)
                throw new Exception($"{ttsResponse.StatusCode}: {ttsJson}");

            using var ttsDoc = JsonDocument.Parse(ttsJson);
            var generationId = ttsDoc.RootElement.TryGetProperty("generation_id", out var genProp)
                ? genProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(generationId))
                throw new Exception("Audixa did not return a generation_id.");

            // 3️⃣ Poll status until completed
            string? downloadUrl = null;
            while (downloadUrl == null)
            {
                var statusUri = $"{STATUS_URL}?generation_id={Uri.EscapeDataString(generationId)}";
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUri);
                statusRequest.Headers.Add("x-api-key", settings.ApiKey);
                statusRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var statusResponse = await client.SendAsync(statusRequest, cancellationToken);
                var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!statusResponse.IsSuccessStatusCode)
                    throw new Exception($"{statusResponse.StatusCode}: {statusJson}");

                using var statusDoc = JsonDocument.Parse(statusJson);
                var status = statusDoc.RootElement.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : null;

                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = statusDoc.RootElement.TryGetProperty("url", out var urlProp)
                        ? urlProp.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(downloadUrl))
                        throw new Exception("Audixa returned Completed without a download url.");

                    break;
                }

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var detail = statusDoc.RootElement.TryGetProperty("detail", out var detailProp)
                        ? detailProp.GetString()
                        : "Audixa generation failed.";
                    throw new Exception(detail);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            // 4️⃣ Download completed audio
            using var audioResponse = await client.GetAsync(downloadUrl, cancellationToken);
            var audioBytes = await audioResponse.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!audioResponse.IsSuccessStatusCode)
                throw new Exception($"{audioResponse.StatusCode}: failed to download generated audio.");

            // 5️⃣ Upload and return resource link only
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.mp3",
                BinaryData.FromBytes(audioBytes),
                cancellationToken);

            if (uploaded == null)
                throw new Exception("Audio upload failed");

            return uploaded.ToResourceLinkCallToolResponse();
        });

    [Description("Please fill in the Audixa text-to-speech request.")]
    public class AudixaTTSRequest
    {
        [Required]
        [JsonPropertyName("text")]
        [MaxLength(5000)]
        [Description("The text to convert to speech.")]
        public string Text { get; set; } = default!;

        [Required]
        [JsonPropertyName("voice")]
        [Description("Audixa voice ID from /voices.")]
        public string Voice { get; set; } = default!;

        [Required]
        [JsonPropertyName("model")]
        [Description("Model tier: base or advance.")]
        public string Model { get; set; } = "base";

        [JsonPropertyName("speed")]
        [Range(0.5, 2.0)]
        [Description("Playback speed multiplier (0.5 - 2.0).")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("emotion")]
        [Description("Emotion tone for advance model.")]
        public string Emotion { get; set; } = "neutral";

        [JsonPropertyName("temperature")]
        [Range(0.7, 1.0)]
        [Description("Creativity control for advance model (0.7 - 1.0).")]
        public double Temperature { get; set; } = 0.9;

        [JsonPropertyName("top_p")]
        [Range(0.7, 0.98)]
        [Description("Plausibility filter for advance model (0.7 - 0.98).")]
        public double TopP { get; set; } = 0.9;

        [Required]
        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

public class AudixaSettings
{
    public string ApiKey { get; set; } = default!;
}
