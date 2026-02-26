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

namespace MCPhappey.Tools.Deepgram.Audio;

public static class DeepgramAudio
{
    private const string SpeakUrl = "https://api.deepgram.com/v1/speak";
    private const string ListenUrl = "https://api.deepgram.com/v1/listen";

    [Description("Convert text into natural-sounding speech using Deepgram text-to-speech.")]
    [McpServerTool(
        Title = "Deepgram Text-to-Speech",
        Name = "deepgram_audio_text_to_speech",
        Destructive = false)]
    public static async Task<CallToolResult?> DeepgramAudio_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string text,
        [Description("Voice/model, e.g. aura-2-thalia-en.")] string model = "aura-2-thalia-en",
        [Description("Audio encoding: linear16, flac, mulaw, alaw, mp3, opus, aac.")] string encoding = "mp3",
        [Description("Container: none, wav, ogg.")] string container = "none",
        [Description("Sample rate in Hz.")] int sampleRate = 48000,
        [Description("Bitrate in bits/sec.")] int bitRate = 48000,
        [Description("Tag for usage reporting.")] string? tag = null,
        [Description("Opt out of Deepgram MIP.")] bool mipOptOut = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<DeepgramSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Elicit before TTS call to confirm settings
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new DeepgramTextToSpeechRequest
                {
                    Text = text,
                    Model = model,
                    Encoding = encoding,
                    Container = container,
                    SampleRate = sampleRate,
                    BitRate = bitRate,
                    Tag = tag,
                    MipOptOut = mipOptOut,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post,
                BuildUrlWithQuery(SpeakUrl, new Dictionary<string, string?>
                {
                    ["model"] = typed.Model,
                    ["encoding"] = typed.Encoding,
                    ["container"] = typed.Container,
                    ["sample_rate"] = typed.SampleRate.ToString(),
                    ["bit_rate"] = typed.BitRate.ToString(),
                    ["tag"] = typed.Tag,
                    ["mip_opt_out"] = typed.MipOptOut.ToString().ToLowerInvariant(),
                }));

            req.Headers.TryAddWithoutValidation("Authorization", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));
            req.Content = new StringContent(JsonSerializer.Serialize(new { text = typed.Text }), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {Encoding.UTF8.GetString(bytesOut)}");

            var ext = typed.Encoding.ToLowerInvariant() switch
            {
                "linear16" => "wav",
                "flac" => "flac",
                "mulaw" => "wav",
                "alaw" => "wav",
                "opus" => "opus",
                "aac" => "aac",
                _ => "mp3"
            };

            var mime = ext switch
            {
                "wav" => "audio/wav",
                "flac" => "audio/flac",
                "opus" => "audio/opus",
                "aac" => "audio/aac",
                _ => "audio/mpeg"
            };

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.{ext}",
                BinaryData.FromBytes(bytesOut),
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
                        Data = bytesOut,
                        MimeType = mime
                    }
                ]
            };
        });

    [Description("Transcribe pre-recorded audio/video using Deepgram speech-to-text.")]
    [McpServerTool(
        Title = "Deepgram Speech-to-Text",
        Name = "deepgram_audio_transcribe_audio",
        Destructive = false)]
    public static async Task<CallToolResult?> DeepgramAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to transcribe.")] string fileUrl,
        [Description("Model name, e.g. nova-3.")] string model = "nova-3",
        [Description("Language hint (BCP-47), e.g. en or nl.")] string language = "en",
        [Description("Auto detect language.")] bool detectLanguage = false,
        [Description("Enable diarization.")] bool diarize = false,
        [Description("Add punctuation/capitalization.")] bool punctuate = true,
        [Description("Apply smart formatting.")] bool smartFormat = true,
        [Description("Return utterance segmentation.")] bool utterances = false,
        [Description("Split into paragraphs.")] bool paragraphs = false,
        [Description("Convert written numbers to numerals.")] bool numerals = false,
        [Description("Treat audio as multi-channel.")] bool multichannel = false,
        [Description("Summarize transcript.")] bool summarize = false,
        [Description("Analyze sentiment.")] bool sentiment = false,
        [Description("Analyze topics.")] bool topics = false,
        [Description("Analyze intents.")] bool intents = false,
        [Description("Comma-separated custom topics.")] string? customTopic = null,
        [Description("Custom topic mode: extended or strict.")] string customTopicMode = "extended",
        [Description("Comma-separated custom intents.")] string? customIntent = null,
        [Description("Custom intent mode: extended or strict.")] string customIntentMode = "extended",
        [Description("Tag for usage reporting.")] string? tag = null,
        [Description("Opt out of Deepgram MIP.")] bool mipOptOut = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<DeepgramSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            _ = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio or video content.");

            var payload = new { url = fileUrl };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post,
                BuildUrlWithQuery(ListenUrl, new Dictionary<string, string?>
                {
                    ["model"] = model,
                    ["language"] = language,
                    ["detect_language"] = detectLanguage ? "true" : null,
                    ["diarize"] = diarize.ToString().ToLowerInvariant(),
                    ["punctuate"] = punctuate.ToString().ToLowerInvariant(),
                    ["smart_format"] = smartFormat.ToString().ToLowerInvariant(),
                    ["utterances"] = utterances.ToString().ToLowerInvariant(),
                    ["paragraphs"] = paragraphs.ToString().ToLowerInvariant(),
                    ["numerals"] = numerals.ToString().ToLowerInvariant(),
                    ["multichannel"] = multichannel.ToString().ToLowerInvariant(),
                    ["summarize"] = summarize.ToString().ToLowerInvariant(),
                    ["sentiment"] = sentiment.ToString().ToLowerInvariant(),
                    ["topics"] = topics.ToString().ToLowerInvariant(),
                    ["intents"] = intents.ToString().ToLowerInvariant(),
                    ["custom_topic"] = customTopic,
                    ["custom_topic_mode"] = customTopicMode,
                    ["custom_intent"] = customIntent,
                    ["custom_intent_mode"] = customIntentMode,
                    ["tag"] = tag,
                    ["mip_opt_out"] = mipOptOut.ToString().ToLowerInvariant(),
                }));

            req.Headers.TryAddWithoutValidation("Authorization", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            var transcript = ExtractTranscript(json);
            if (string.IsNullOrWhiteSpace(transcript))
                transcript = "No transcript text found in Deepgram response.";

            var safeName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();
            var uploadedTxt = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.txt",
                BinaryData.FromString(transcript),
                cancellationToken);

            var uploadedJson = await requestContext.Server.Upload(
                serviceProvider,
                $"{safeName}.json",
                BinaryData.FromString(json),
                cancellationToken);

            return new CallToolResult
            {
                Content =
                [
                    transcript.ToTextContentBlock(),
                    uploadedTxt!,
                    uploadedJson!,
                ]
            };
        });

    [Description("Please fill in the Deepgram text-to-speech request.")]
    public class DeepgramTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Voice/model, e.g. aura-2-thalia-en.")]
        public string Model { get; set; } = "aura-2-thalia-en";

        [JsonPropertyName("encoding")]
        [Description("Audio encoding.")]
        public string Encoding { get; set; } = "mp3";

        [JsonPropertyName("container")]
        [Description("Audio container.")]
        public string Container { get; set; } = "none";

        [JsonPropertyName("sample_rate")]
        [Description("Sample rate in Hz.")]
        public int SampleRate { get; set; } = 48000;

        [JsonPropertyName("bit_rate")]
        [Description("Bitrate in bits/sec.")]
        public int BitRate { get; set; } = 48000;

        [JsonPropertyName("tag")]
        [Description("Optional usage tag.")]
        public string? Tag { get; set; }

        [JsonPropertyName("mip_opt_out")]
        [Description("Opt out of MIP.")]
        public bool MipOptOut { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    private static string BuildUrlWithQuery(string baseUrl, IDictionary<string, string?> query)
    {
        var parts = query
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}")
            .ToList();

        if (parts.Count == 0)
            return baseUrl;

        return $"{baseUrl}?{string.Join("&", parts)}";
    }

    private static string? ExtractTranscript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("results", out var results))
            return null;

        if (!results.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() == 0)
            return null;

        var channel0 = channels[0];
        if (!channel0.TryGetProperty("alternatives", out var alternatives) || alternatives.ValueKind != JsonValueKind.Array || alternatives.GetArrayLength() == 0)
            return null;

        var alt0 = alternatives[0];
        if (!alt0.TryGetProperty("transcript", out var transcript))
            return null;

        return transcript.GetString();
    }
}
