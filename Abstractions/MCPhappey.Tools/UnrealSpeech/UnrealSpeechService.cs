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

namespace MCPhappey.Tools.UnrealSpeech;

public static class UnrealSpeechService
{
    private const string SpeechUrl = "https://api.v8.unrealspeech.com/speech";
    private const string SynthesisTasksUrl = "https://api.v8.unrealspeech.com/synthesisTasks";

    [Description("Generate speech audio from a fileUrl by scraping text first, then calling UnrealSpeech /speech and uploading the resulting audio.")]
    [McpServerTool(
        Title = "UnrealSpeech /speech File-to-Speech",
        Name = "unrealspeech_speech_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> UnrealSpeech_Speech_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("UnrealSpeech VoiceId. Default: Sierra.")]
        string voice_id = "Sierra",
        [Description("Bitrate. Allowed: 16k, 32k, 48k, 64k, 128k, 192k, 256k, 320k. Default: 192k.")]
        string bitrate = "192k",
        [Description("Speech speed from -1.0 to 1.0. Default: 0.")]
        double speed = 0,
        [Description("Speech pitch from 0.5 to 1.5. Default: 1.0.")]
        double pitch = 1.0,
        [Description("Timestamp granularity: word or sentence. Default: sentence.")]
        string timestamp_type = "sentence",
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var sourceText = await ScrapeTextFromFileUrlAsync(serviceProvider, requestContext, fileUrl, cancellationToken);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new UnrealSpeechSpeechFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = string.IsNullOrWhiteSpace(voice_id) ? "Sierra" : voice_id.Trim(),
                    Bitrate = NormalizeBitrate(bitrate),
                    Speed = ClampSpeed(speed),
                    Pitch = ClampPitch(pitch),
                    TimestampType = NormalizeTimestampType(timestamp_type),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp3")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (sourceText.Length > 3000)
                throw new ValidationException("The /speech endpoint accepts up to 3,000 characters. Use the /synthesisTasks tool for longer content.");

            var payload = new
            {
                Text = sourceText,
                VoiceId = typed.VoiceId,
                Bitrate = typed.Bitrate,
                Speed = typed.Speed,
                Pitch = typed.Pitch,
                AudioFormat = "mp3",
                OutputFormat = "uri",
                TimestampType = typed.TimestampType
            };

            var json = await PostJsonAsync(serviceProvider, SpeechUrl, payload, cancellationToken);
            var outputUri = ResolveOutputUri(json)
                ?? throw new InvalidOperationException("UnrealSpeech /speech did not return OutputUri.");

            var bytes = await DownloadBytesAsync(serviceProvider, outputUri, cancellationToken);
            var uploadName = EnsureExtension(typed.Filename, "mp3");

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Generate speech audio from a fileUrl by scraping text first, then calling UnrealSpeech /synthesisTasks, polling to completion, downloading audio, and uploading it.")]
    [McpServerTool(
        Title = "UnrealSpeech /synthesisTasks File-to-Speech",
        Name = "unrealspeech_synthesis_tasks_file_to_speech",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> UnrealSpeech_SynthesisTasks_FileToSpeech(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL (SharePoint, OneDrive, HTTP) to extract text from.")]
        string fileUrl,
        [Description("UnrealSpeech VoiceId. Default: Sierra.")]
        string voice_id = "Sierra",
        [Description("Bitrate. Allowed: 16k, 32k, 48k, 64k, 128k, 192k, 256k, 320k. Default: 192k.")]
        string bitrate = "192k",
        [Description("Speech speed from -1.0 to 1.0. Default: 0.")]
        double speed = 0,
        [Description("Speech pitch from 0.5 to 1.5. Default: 1.0.")]
        double pitch = 1.0,
        [Description("Timestamp granularity: word or sentence. Default: sentence.")]
        string timestamp_type = "sentence",
        [Description("Polling interval in seconds. Default: 3.")]
        [Range(1, 60)]
        int poll_interval_seconds = 3,
        [Description("Maximum wait time in seconds. Default: 900.")]
        [Range(30, 3600)]
        int max_wait_seconds = 900,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

            var sourceText = await ScrapeTextFromFileUrlAsync(serviceProvider, requestContext, fileUrl, cancellationToken);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new UnrealSpeechSynthesisTasksFileToSpeechRequest
                {
                    FileUrl = fileUrl,
                    VoiceId = string.IsNullOrWhiteSpace(voice_id) ? "Sierra" : voice_id.Trim(),
                    Bitrate = NormalizeBitrate(bitrate),
                    Speed = ClampSpeed(speed),
                    Pitch = ClampPitch(pitch),
                    TimestampType = NormalizeTimestampType(timestamp_type),
                    PollIntervalSeconds = Math.Clamp(poll_interval_seconds, 1, 60),
                    MaxWaitSeconds = Math.Clamp(max_wait_seconds, 30, 3600),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp3")
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (sourceText.Length > 500000)
                throw new ValidationException("The /synthesisTasks endpoint accepts up to 500,000 characters.");

            var createPayload = new
            {
                Text = sourceText,
                VoiceId = typed.VoiceId,
                Bitrate = typed.Bitrate,
                Speed = typed.Speed,
                Pitch = typed.Pitch,
                AudioFormat = "mp3",
                OutputFormat = "uri",
                TimestampType = typed.TimestampType
            };

            var createJson = await PostJsonAsync(serviceProvider, SynthesisTasksUrl, createPayload, cancellationToken);
            var taskId = ResolveTaskId(createJson)
                ?? throw new InvalidOperationException("UnrealSpeech /synthesisTasks did not return TaskId.");

            var completedJson = await PollSynthesisTaskUntilCompletedAsync(
                serviceProvider,
                taskId,
                typed.PollIntervalSeconds,
                typed.MaxWaitSeconds,
                cancellationToken);

            var outputUri = ResolveOutputUri(completedJson)
                ?? throw new InvalidOperationException("UnrealSpeech synthesis task completed without OutputUri.");

            var bytes = await DownloadBytesAsync(serviceProvider, outputUri, cancellationToken);
            var uploadName = EnsureExtension(typed.Filename, "mp3");

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static async Task<string> ScrapeTextFromFileUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var sourceText = string.Join("\n\n", files.GetTextFiles().Select(f => f.Contents.ToString()));

        if (string.IsNullOrWhiteSpace(sourceText))
            throw new InvalidOperationException("No readable text content found in fileUrl.");

        return sourceText;
    }

    private static async Task<JsonObject> PostJsonAsync(
        IServiceProvider serviceProvider,
        string url,
        object payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<UnrealSpeechSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"UnrealSpeech call failed ({(int)resp.StatusCode}): {body}");

        return JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("UnrealSpeech returned invalid JSON.");
    }

    private static async Task<JsonObject> GetJsonAsync(
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<UnrealSpeechSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var resp = await client.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"UnrealSpeech status call failed ({(int)resp.StatusCode}): {body}");

        return JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("UnrealSpeech returned invalid JSON.");
    }

    private static async Task<JsonObject> PollSynthesisTaskUntilCompletedAsync(
        IServiceProvider serviceProvider,
        string taskId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (true)
        {
            var statusJson = await GetJsonAsync(serviceProvider, $"{SynthesisTasksUrl}/{taskId}", timeoutCts.Token);
            var status = ResolveTaskStatus(statusJson)?.Trim().ToLowerInvariant();

            if (status is "completed" or "done" or "success" or "succeeded")
                return statusJson;

            if (status is "failed" or "error" or "cancelled" or "canceled")
            {
                var details = GetString(statusJson, "StatusDetails")
                    ?? GetString(statusJson["SynthesisTask"] as JsonObject, "StatusDetails")
                    ?? "No status details provided.";
                throw new InvalidOperationException($"UnrealSpeech synthesis task failed: {details}");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }
    }

    private static async Task<byte[]> DownloadBytesAsync(
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken)
    {
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = clientFactory.CreateClient();
        using var resp = await client.GetAsync(url, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Failed to download UnrealSpeech output ({(int)resp.StatusCode}): {body}");
        }

        return bytes;
    }

    private static string? ResolveTaskId(JsonObject json)
        => GetString(json, "TaskId")
           ?? GetString(json["SynthesisTask"] as JsonObject, "TaskId");

    private static string? ResolveTaskStatus(JsonObject json)
        => GetString(json, "TaskStatus")
           ?? GetString(json["SynthesisTask"] as JsonObject, "TaskStatus");

    private static string? ResolveOutputUri(JsonObject json)
    {
        var rootValue = json["OutputUri"];
        if (rootValue is JsonValue rootScalar)
            return rootScalar.GetValue<string>();

        if (rootValue is JsonArray rootArray)
            return rootArray.Select(a => a?.GetValue<string>()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (json["SynthesisTask"] is JsonObject synthesis)
        {
            var nested = synthesis["OutputUri"];
            if (nested is JsonValue scalar)
                return scalar.GetValue<string>();

            if (nested is JsonArray array)
                return array.Select(a => a?.GetValue<string>()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        return null;
    }

    private static string? GetString(JsonObject? json, string propertyName)
        => json?[propertyName] is JsonValue value ? value.GetValue<string>() : null;

    private static string NormalizeBitrate(string? bitrate)
    {
        var value = (bitrate ?? "192k").Trim().ToLowerInvariant();
        return value is "16k" or "32k" or "48k" or "64k" or "128k" or "192k" or "256k" or "320k"
            ? value
            : "192k";
    }

    private static double ClampSpeed(double speed)
        => Math.Clamp(speed, -1.0, 1.0);

    private static double ClampPitch(double pitch)
        => Math.Clamp(pitch, 0.5, 1.5);

    private static string NormalizeTimestampType(string? timestampType)
    {
        var value = (timestampType ?? "sentence").Trim().ToLowerInvariant();
        return value is "word" or "sentence" ? value : "sentence";
    }

    private static string EnsureExtension(string filename, string extension)
    {
        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        return filename.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)
            ? filename
            : $"{filename}.{ext}";
    }

    [Description("Please fill in the UnrealSpeech /speech file-to-speech request.")]
    public sealed class UnrealSpeechSpeechFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("VoiceId")]
        [Required]
        [Description("UnrealSpeech VoiceId.")]
        public string VoiceId { get; set; } = "Sierra";

        [JsonPropertyName("Bitrate")]
        [Description("Bitrate: 16k, 32k, 48k, 64k, 128k, 192k, 256k, 320k.")]
        public string Bitrate { get; set; } = "192k";

        [JsonPropertyName("Speed")]
        [Description("Speech speed from -1.0 to 1.0.")]
        public double Speed { get; set; } = 0;

        [JsonPropertyName("Pitch")]
        [Description("Speech pitch from 0.5 to 1.5.")]
        public double Pitch { get; set; } = 1.0;

        [JsonPropertyName("TimestampType")]
        [Description("Timestamp granularity: word or sentence.")]
        public string TimestampType { get; set; } = "sentence";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the UnrealSpeech /synthesisTasks file-to-speech request.")]
    public sealed class UnrealSpeechSynthesisTasksFileToSpeechRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Source file URL to scrape/extract text from.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("VoiceId")]
        [Required]
        [Description("UnrealSpeech VoiceId.")]
        public string VoiceId { get; set; } = "Sierra";

        [JsonPropertyName("Bitrate")]
        [Description("Bitrate: 16k, 32k, 48k, 64k, 128k, 192k, 256k, 320k.")]
        public string Bitrate { get; set; } = "192k";

        [JsonPropertyName("Speed")]
        [Description("Speech speed from -1.0 to 1.0.")]
        public double Speed { get; set; } = 0;

        [JsonPropertyName("Pitch")]
        [Description("Speech pitch from 0.5 to 1.5.")]
        public double Pitch { get; set; } = 1.0;

        [JsonPropertyName("TimestampType")]
        [Description("Timestamp granularity: word or sentence.")]
        public string TimestampType { get; set; } = "sentence";

        [JsonPropertyName("pollIntervalSeconds")]
        [Range(1, 60)]
        [Description("Polling interval in seconds.")]
        public int PollIntervalSeconds { get; set; } = 3;

        [JsonPropertyName("maxWaitSeconds")]
        [Range(30, 3600)]
        [Description("Maximum wait time in seconds.")]
        public int MaxWaitSeconds { get; set; } = 900;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }
}

