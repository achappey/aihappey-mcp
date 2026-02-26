using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

namespace MCPhappey.Tools.SmallestAI;

public static class SmallestAISpeech
{
    private const string LightningV2Path = "api/v1/lightning-v2/get_speech";
    private const string LightningV31StreamPath = "api/v1/lightning-v3.1/stream";

    [Description("Generate speech with Smallest AI from text or fileUrl, upload the audio result, and return only a resource link block.")]
    [McpServerTool(
        Title = "Smallest AI Speech",
        Name = "smallestai_speech_generate",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SmallestAI_Speech_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to convert to speech. If omitted, fileUrl is used as source text.")] string? text = null,
        [Description("Optional source file URL (SharePoint, OneDrive, HTTP) to scrape text from.")] string? fileUrl = null,
        [Description("Model: lightning-v2 or lightning-v3.1. Default: lightning-v2.")] string model = "lightning-v2",
        [Description("Voice ID to use. Default: emily.")] string voice_id = "emily",
        [Description("Sample rate. lightning-v2: 8000-24000. lightning-v3.1: 8000,16000,24000,44100.")] int sample_rate = 24000,
        [Description("Speech speed from 0.5 to 2.0. Default: 1.0.")] double speed = 1.0,
        [Description("Language code (e.g. auto, en, hi, ta, es). Default: auto.")] string language = "auto",
        [Description("Output format: pcm, mp3, wav, mulaw. Default: wav.")] string output_format = "wav",
        [Description("Optional pronunciation dictionary IDs, comma-separated.")] string? pronunciation_dicts = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var resolvedText = await ResolveInputTextAsync(serviceProvider, requestContext, text, fileUrl, cancellationToken);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SmallestAISpeechRequest
                {
                    Text = resolvedText,
                    FileUrl = NormalizeOptional(fileUrl),
                    Model = NormalizeModel(model),
                    VoiceId = string.IsNullOrWhiteSpace(voice_id) ? "emily" : voice_id.Trim(),
                    SampleRate = NormalizeSampleRate(sample_rate, NormalizeModel(model)),
                    Speed = Math.Clamp(speed, 0.5, 2.0),
                    Language = NormalizeLanguage(language),
                    OutputFormat = NormalizeOutputFormat(output_format),
                    PronunciationDicts = NormalizeOptional(pronunciation_dicts),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var bytes = await GenerateSpeechBytesAsync(serviceProvider, typed, cancellationToken);
            if (bytes.Length == 0)
                throw new InvalidOperationException("Smallest AI speech generation returned empty audio data.");

            var ext = NormalizeOutputFormat(typed.OutputFormat);
            var uploadName = typed.Filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
                ? typed.Filename
                : $"{typed.Filename}.{ext}";

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static async Task<string> ResolveInputTextAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? text,
        string? fileUrl,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(text))
            return text.Trim();

        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("Either text or fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

        if (string.IsNullOrWhiteSpace(sourceText))
            throw new InvalidOperationException("No readable text content found in fileUrl.");

        return sourceText;
    }

    private static async Task<byte[]> GenerateSpeechBytesAsync(
        IServiceProvider serviceProvider,
        SmallestAISpeechRequest typed,
        CancellationToken cancellationToken)
    {
        using var client = serviceProvider.CreateSmallestAIClient();
        var endpoint = typed.Model == "lightning-v3.1" ? LightningV31StreamPath : LightningV2Path;

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildSpeechBodyJson(typed), Encoding.UTF8, MimeTypes.Json)
        };

        if (typed.Model == "lightning-v3.1")
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Smallest AI speech stream failed ({(int)resp.StatusCode}): {err}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            return await ReadSseAudioAsync(stream, cancellationToken);
        }

        using var response = await client.SendAsync(req, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Smallest AI speech failed ({(int)response.StatusCode}): {err}");
        }

        return bytes;
    }

    private static string BuildSpeechBodyJson(SmallestAISpeechRequest typed)
    {
        var payload = new Dictionary<string, object?>
        {
            ["text"] = typed.Text,
            ["voice_id"] = typed.VoiceId,
            ["sample_rate"] = typed.SampleRate,
            ["speed"] = typed.Speed,
            ["language"] = typed.Language,
            ["output_format"] = typed.OutputFormat
        };

        if (!string.IsNullOrWhiteSpace(typed.PronunciationDicts))
        {
            payload["pronunciation_dicts"] = typed.PronunciationDicts
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        return JsonSerializer.Serialize(payload);
    }

    private static async Task<byte[]> ReadSseAudioAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        var output = new List<byte>(64 * 1024);
        string? currentEvent = null;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                break;

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("done:", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(line[5..].Trim(), out var doneFlag)
                && doneFlag)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].Trim();
            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
                break;

            if (TryHandleJsonSseData(data, output, out var doneJson) && doneJson)
                break;

            if (string.Equals(currentEvent, "chunk", StringComparison.OrdinalIgnoreCase))
                TryAppendBase64(data, output);
        }

        return output.ToArray();
    }

    private static bool TryHandleJsonSseData(string data, List<byte> output, out bool done)
    {
        done = false;
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True)
                done = true;

            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
                TryAppendBase64(dataEl.GetString(), output);
            else if (root.TryGetProperty("chunk", out var chunkEl) && chunkEl.ValueKind == JsonValueKind.String)
                TryAppendBase64(chunkEl.GetString(), output);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryAppendBase64(string? base64, List<byte> output)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return;

        try
        {
            output.AddRange(Convert.FromBase64String(base64));
        }
        catch
        {
            // Ignore malformed chunks and continue stream processing.
        }
    }

    private static string NormalizeModel(string? model)
    {
        var value = (model ?? "lightning-v2").Trim().ToLowerInvariant();
        return value is "lightning-v2" or "lightning-v3.1" ? value : "lightning-v2";
    }

    private static int NormalizeSampleRate(int value, string model)
    {
        if (model == "lightning-v3.1")
        {
            var valid = new[] { 8000, 16000, 24000, 44100 };
            return valid.Contains(value) ? value : 44100;
        }

        return Math.Clamp(value, 8000, 24000);
    }

    private static string NormalizeLanguage(string? value)
    {
        var language = (value ?? "auto").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(language))
            return "auto";

        return language;
    }

    private static string NormalizeOutputFormat(string? value)
    {
        var format = (value ?? "wav").Trim().ToLowerInvariant();
        return format is "pcm" or "mp3" or "wav" or "mulaw" ? format : "wav";
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [Description("Please fill in the Smallest AI speech request.")]
    public sealed class SmallestAISpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("fileUrl")]
        [Description("Optional source file URL when text is extracted from document content.")]
        public string? FileUrl { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("lightning-v2 or lightning-v3.1.")]
        public string Model { get; set; } = "lightning-v2";

        [JsonPropertyName("voice_id")]
        [Required]
        [Description("Voice ID.")]
        public string VoiceId { get; set; } = "emily";

        [JsonPropertyName("sample_rate")]
        [Description("Output sample rate.")]
        public int SampleRate { get; set; } = 24000;

        [JsonPropertyName("speed")]
        [Range(0.5, 2.0)]
        [Description("Speech speed.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("language")]
        [Required]
        [Description("Language code.")]
        public string Language { get; set; } = "auto";

        [JsonPropertyName("output_format")]
        [Required]
        [Description("pcm, mp3, wav, mulaw.")]
        public string OutputFormat { get; set; } = "wav";

        [JsonPropertyName("pronunciation_dicts")]
        [Description("Optional comma-separated pronunciation dictionary IDs.")]
        public string? PronunciationDicts { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

