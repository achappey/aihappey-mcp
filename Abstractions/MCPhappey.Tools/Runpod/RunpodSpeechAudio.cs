using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Runpod;

public static class RunpodSpeechAudio
{
    private const string ChatterboxTurboRunSync = "https://api.runpod.ai/v2/chatterbox-turbo/runsync";
    private const string MiniMaxSpeech02HdRunSync = "https://api.runpod.ai/v2/minimax-speech-02-hd/runsync";

    [Description("Generate speech audio from raw text using Runpod and upload the result as a resource link.")]
    [McpServerTool(
        Title = "Runpod Text-to-Speech",
        Name = "runpod_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> RunpodSpeech_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize into speech.")] string input,
        [Description("Runpod endpoint. Options: chatterbox-turbo (default), minimax-speech-02-hd.")]
        RunpodSpeechEndpoint endpoint = RunpodSpeechEndpoint.ChatterboxTurbo,
        [Description("Voice identifier. Maps to 'voice' for Chatterbox Turbo and 'voice_id' for MiniMax Speech 02 HD.")]
        string? voice = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new RunpodSpeechTextToSpeechRequest
                {
                    Input = input,
                    Endpoint = endpoint,
                    Voice = voice,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Input,
                typed.Endpoint,
                typed.Voice,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then synthesizing with Runpod.")]
    [McpServerTool(
        Title = "Runpod File-to-Speech",
        Name = "runpod_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> RunpodSpeech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("Runpod endpoint. Options: chatterbox-turbo (default), minimax-speech-02-hd.")]
        RunpodSpeechEndpoint endpoint = RunpodSpeechEndpoint.ChatterboxTurbo,
        [Description("Voice identifier. Maps to 'voice' for Chatterbox Turbo and 'voice_id' for MiniMax Speech 02 HD.")]
        string? voice = null,
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
                new RunpodSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Endpoint = endpoint,
                    Voice = voice,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.Endpoint,
                typed.Voice,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string input,
        RunpodSpeechEndpoint endpoint,
        string? voice,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var settings = serviceProvider.GetRequiredService<RunpodSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = clientFactory.CreateClient();

        var targetUrl = endpoint == RunpodSpeechEndpoint.MiniMaxSpeech02Hd
            ? MiniMaxSpeech02HdRunSync
            : ChatterboxTurboRunSync;

        var payload = BuildPayload(input, endpoint, voice);

        using var req = new HttpRequestMessage(HttpMethod.Post, targetUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Runpod speech call failed ({(int)resp.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String
            ? statusEl.GetString()
            : null;

        if (string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "TIMED_OUT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Runpod speech generation failed with status '{status}': {root.GetRawText()}");
        }

        var audioUrl = ExtractAudioUrl(root);
        if (string.IsNullOrWhiteSpace(audioUrl))
            throw new InvalidOperationException("Runpod speech response did not include output.audio_url.");

        using var mediaResp = await client.GetAsync(audioUrl, cancellationToken);
        var bytes = await mediaResp.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!mediaResp.IsSuccessStatusCode)
        {
            var text = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Runpod speech download failed ({(int)mediaResp.StatusCode}): {text}");
        }

        var ext = GuessAudioExtension(audioUrl, mediaResp.Content.Headers.ContentType?.MediaType) ?? "wav";
        var uploadName = filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static JsonObject BuildPayload(string input, RunpodSpeechEndpoint endpoint, string? voice)
    {
        var normalizedVoice = string.IsNullOrWhiteSpace(voice) ? null : voice.Trim();

        JsonObject payloadInput;
        if (endpoint == RunpodSpeechEndpoint.MiniMaxSpeech02Hd)
        {
            payloadInput = new JsonObject
            {
                ["prompt"] = input
            };

            if (!string.IsNullOrWhiteSpace(normalizedVoice))
                payloadInput["voice_id"] = normalizedVoice;
        }
        else
        {
            payloadInput = new JsonObject
            {
                ["prompt"] = input
            };

            if (!string.IsNullOrWhiteSpace(normalizedVoice))
                payloadInput["voice"] = normalizedVoice;
        }

        return new JsonObject
        {
            ["input"] = payloadInput
        };
    }

    private static string? ExtractAudioUrl(JsonElement root)
    {
        if (root.TryGetProperty("output", out var outputEl)
            && outputEl.ValueKind == JsonValueKind.Object
            && outputEl.TryGetProperty("audio_url", out var audioUrlEl)
            && audioUrlEl.ValueKind == JsonValueKind.String)
        {
            var url = audioUrlEl.GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        }

        if (root.TryGetProperty("audio_url", out var rootAudioUrlEl)
            && rootAudioUrlEl.ValueKind == JsonValueKind.String)
        {
            var url = rootAudioUrlEl.GetString();
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;
        }

        return null;
    }

    private static string? GuessAudioExtension(string? audioUrl, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(audioUrl))
        {
            if (audioUrl.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return "wav";
            if (audioUrl.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)) return "flac";
            if (audioUrl.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return "ogg";
            if (audioUrl.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return "mp3";
            if (audioUrl.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)) return "m4a";
            if (audioUrl.EndsWith(".aac", StringComparison.OrdinalIgnoreCase)) return "aac";
        }

        var mt = mediaType?.Trim().ToLowerInvariant();
        return mt switch
        {
            "audio/wav" or "audio/x-wav" => "wav",
            "audio/flac" => "flac",
            "audio/ogg" => "ogg",
            "audio/mpeg" => "mp3",
            "audio/mp4" => "m4a",
            "audio/aac" => "aac",
            _ => null
        };
    }

    [Description("Please fill in the Runpod text-to-speech request.")]
    public sealed class RunpodSpeechTextToSpeechRequest
    {
        [JsonPropertyName("input")]
        [Required]
        [Description("Plain text to synthesize.")]
        public string Input { get; set; } = default!;

        [JsonPropertyName("endpoint")]
        [Required]
        [Description("Runpod endpoint selector. Default: chatterbox-turbo.")]
        public RunpodSpeechEndpoint Endpoint { get; set; } = RunpodSpeechEndpoint.ChatterboxTurbo;

        [JsonPropertyName("voice")]
        [Description("Optional voice identifier. Mapped per endpoint.")]
        public string? Voice { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Runpod file-to-speech request.")]
    public sealed class RunpodSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("endpoint")]
        [Required]
        [Description("Runpod endpoint selector. Default: chatterbox-turbo.")]
        public RunpodSpeechEndpoint Endpoint { get; set; } = RunpodSpeechEndpoint.ChatterboxTurbo;

        [JsonPropertyName("voice")]
        [Description("Optional voice identifier. Mapped per endpoint.")]
        public string? Voice { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunpodSpeechEndpoint
{
    [JsonStringEnumMemberName("chatterbox-turbo")] ChatterboxTurbo,
    [JsonStringEnumMemberName("minimax-speech-02-hd")] MiniMaxSpeech02Hd
}
