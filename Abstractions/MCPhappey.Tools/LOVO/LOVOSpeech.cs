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
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.LOVO;

public static class LOVOSpeech
{
    private const string BaseUrl = "https://api.genny.lovo.ai";
    private const string SyncTtsPath = "/api/v1/tts/sync";
    private const string RetrieveJobPath = "/api/v1/tts/{0}";
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 300;

    [Description("Generate speech audio from raw text using LOVO Genny and upload the result as a resource link.")]
    [McpServerTool(
        Title = "LOVO Text-to-Speech",
        Name = "lovo_speech_text_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> LOVO_TextToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text to synthesize. LOVO supports 1..500 chars; longer input is truncated to 500.")] string text,
        [Description("LOVO speaker id (required).")][Required] string speaker,
        [Description("Optional LOVO speaker style id.")] string? speakerStyle = null,
        [Description("Speech speed from 0.05 to 3.0. Default: 1.0.")] double speed = 1,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LOVOTextToSpeechRequest
                {
                    Text = text,
                    Speaker = speaker,
                    SpeakerStyle = NormalizeOptional(speakerStyle),
                    Speed = NormalizeSpeed(speed),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp3")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                typed.Text,
                typed.Speaker,
                typed.SpeakerStyle,
                typed.Speed,
                DefaultPollIntervalSeconds,
                DefaultMaxWaitSeconds,
                typed.Filename,
                cancellationToken);
        });

    [Description("Generate speech audio from a fileUrl by scraping text first with built-in MCP scraping, then synthesizing with LOVO Genny.")]
    [McpServerTool(
        Title = "LOVO File-to-Speech",
        Name = "lovo_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> LOVO_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")] string fileUrl,
        [Description("LOVO speaker id (required).")][Required] string speaker,
        [Description("Optional LOVO speaker style id.")] string? speakerStyle = null,
        [Description("Speech speed from 0.05 to 3.0. Default: 1.0.")] double speed = 1,
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
                new LOVOFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    Speaker = speaker,
                    SpeakerStyle = NormalizeOptional(speakerStyle),
                    Speed = NormalizeSpeed(speed),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp3")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            return await GenerateAndUploadSpeechAsync(
                serviceProvider,
                requestContext,
                sourceText,
                typed.Speaker,
                typed.SpeakerStyle,
                typed.Speed,
                DefaultPollIntervalSeconds,
                DefaultMaxWaitSeconds,
                typed.Filename,
                cancellationToken);
        });

    private static async Task<CallToolResult?> GenerateAndUploadSpeechAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string sourceText,
        string speaker,
        string? speakerStyle,
        double speed,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        string filename,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);
        ArgumentException.ThrowIfNullOrWhiteSpace(speaker);

        var text = NormalizeText(sourceText);
        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("text is required and cannot be empty.");

        var settings = serviceProvider.GetRequiredService<LOVOSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();

        var initial = await CreateSyncTtsAsync(
            client,
            settings.ApiKey,
            text,
            speaker,
            speakerStyle,
            NormalizeSpeed(speed),
            cancellationToken);

        var audioUrl = ExtractAudioUrl(initial);
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            var jobId = ExtractJobId(initial)
                ?? throw new Exception($"LOVO did not return audio URL or job id. Payload: {initial?.ToJsonString()}");

            var completed = await PollUntilCompletedAsync(
                client,
                settings.ApiKey,
                jobId,
                pollIntervalSeconds,
                maxWaitSeconds,
                cancellationToken);

            audioUrl = ExtractAudioUrl(completed)
                ?? throw new Exception($"LOVO job completed but no audio URL was found. Payload: {completed?.ToJsonString()}");
        }

        var (audioBytes, contentType) = await DownloadAudioAsync(client, audioUrl, cancellationToken);
        var extension = ResolveAudioExtension(audioUrl, contentType);
        var uploadName = $"{filename}.{extension}";

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(audioBytes),
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static async Task<JsonNode?> CreateSyncTtsAsync(
        HttpClient client,
        string apiKey,
        string text,
        string speaker,
        string? speakerStyle,
        double speed,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["text"] = text,
            ["speaker"] = speaker,
            ["speed"] = speed
        };

        if (!string.IsNullOrWhiteSpace(speakerStyle))
            payload["speakerStyle"] = speakerStyle;

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + SyncTtsPath);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-API-KEY", apiKey);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"LOVO sync TTS failed ({(int)response.StatusCode}): {body}");

        return TryParseJson(body);
    }

    private static async Task<JsonNode?> PollUntilCompletedAsync(
        HttpClient client,
        string apiKey,
        string jobId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        pollIntervalSeconds = Math.Clamp(pollIntervalSeconds, 1, 60);
        maxWaitSeconds = Math.Clamp(maxWaitSeconds, 30, 3600);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BaseUrl + string.Format(RetrieveJobPath, Uri.EscapeDataString(jobId)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("X-API-KEY", apiKey);

            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"LOVO job polling failed ({(int)response.StatusCode}): {body}");

            var node = TryParseJson(body);
            if (node == null)
                throw new Exception("LOVO job polling returned invalid JSON.");

            if (!string.IsNullOrWhiteSpace(ExtractAudioUrl(node)))
                return node;

            var status = ExtractStatus(node);
            if (IsFailedStatus(status))
                throw new Exception($"LOVO job '{jobId}' failed with status '{status}'. Payload: {node.ToJsonString()}");

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }
    }

    private static async Task<(byte[] bytes, string? contentType)> DownloadAudioAsync(
        HttpClient client,
        string audioUrl,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(audioUrl, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Failed to download LOVO audio ({(int)response.StatusCode}).");

        var contentType = response.Content.Headers.ContentType?.MediaType;
        return (bytes, contentType);
    }

    private static string NormalizeText(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length <= 500)
            return text;

        return text[..500];
    }

    private static double NormalizeSpeed(double speed)
        => Math.Clamp(speed, 0.05, 3.0);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ExtractJobId(JsonNode? node)
        => node?["id"]?.GetValue<string>()
           ?? node?["jobId"]?.GetValue<string>()
           ?? node?["job_id"]?.GetValue<string>();

    private static string? ExtractStatus(JsonNode? node)
        => node?["status"]?.GetValue<string>()
           ?? node?["state"]?.GetValue<string>();

    private static bool IsFailedStatus(string? status)
    {
        var value = status?.Trim().ToLowerInvariant();
        return value is "failed" or "error" or "cancelled" or "canceled";
    }

    private static string? ExtractAudioUrl(JsonNode? node)
    {
        if (node == null)
            return null;

        if (node is JsonValue v && v.TryGetValue<string>(out var directUrl) && LooksLikeUrl(directUrl))
            return directUrl;

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Value == null)
                    continue;

                var name = property.Key;
                if (property.Value is JsonValue jsonValue
                    && jsonValue.TryGetValue<string>(out var candidate)
                    && LooksLikeUrl(candidate)
                    && (name.Contains("url", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("audio", StringComparison.OrdinalIgnoreCase)))
                    return candidate;

                var nested = ExtractAudioUrl(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var nested = ExtractAudioUrl(item);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static bool LooksLikeUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    private static JsonNode? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return JsonNode.Parse(text);
    }

    private static string ResolveAudioExtension(string? audioUrl, string? contentType)
    {
        var fromContentType = contentType?.Trim().ToLowerInvariant() switch
        {
            "audio/mpeg" => "mp3",
            "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            "audio/mp4" => "m4a",
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(fromContentType))
            return fromContentType;

        try
        {
            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                var ext = Path.GetExtension(new Uri(audioUrl).AbsolutePath).Trim('.').ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(ext))
                    return ext;
            }
        }
        catch
        {
            // ignore parse failures and fallback to default
        }

        return "mp3";
    }

    [Description("Please review the LOVO text-to-speech request.")]
    public sealed class LOVOTextToSpeechRequest
    {
        [JsonPropertyName("text")]
        [Required]
        [Description("Text to synthesize. LOVO supports up to 500 chars.")]
        public string Text { get; set; } = default!;

        [JsonPropertyName("speaker")]
        [Required]
        [Description("LOVO speaker id.")]
        public string Speaker { get; set; } = default!;

        [JsonPropertyName("speakerStyle")]
        [Description("Optional LOVO speaker style id.")]
        public string? SpeakerStyle { get; set; }

        [JsonPropertyName("speed")]
        [Range(0.05, 3.0)]
        [Description("Speech speed between 0.05 and 3.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please review the LOVO file-to-speech request.")]
    public sealed class LOVOFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("speaker")]
        [Required]
        [Description("LOVO speaker id.")]
        public string Speaker { get; set; } = default!;

        [JsonPropertyName("speakerStyle")]
        [Description("Optional LOVO speaker style id.")]
        public string? SpeakerStyle { get; set; }

        [JsonPropertyName("speed")]
        [Range(0.05, 3.0)]
        [Description("Speech speed between 0.05 and 3.0.")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

