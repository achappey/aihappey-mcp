using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Speechmatics;

public static class SpeechmaticsAudio
{
    private const string BaseUrl = "https://asr.api.speechmatics.com/v2";

    [Description("Create a Speechmatics transcription job from fileUrl and wait for completion.")]
    [McpServerTool(
        Title = "Speechmatics Speech-to-Text",
        Name = "speechmatics_audio_transcribe_audio",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> SpeechmaticsAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to transcribe (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        [Description("Optional language hint (e.g. en, nl).")]
        string? language = null,
        [Description("Polling interval in seconds.")]
        int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds before timeout.")]
        int maxWaitSeconds = 900,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

                pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
                maxWaitSeconds = Math.Max(30, maxWaitSeconds);

                var settings = serviceProvider.GetRequiredService<SpeechmaticsSettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var mediaFile = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download audio or video content from fileUrl.");

                using var client = clientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                string? jobId = null;
                string? createJobJson = null;
                string? finalJobJson = null;
                string? transcriptRaw = null;

                // 1) Create job
                using (var form = new MultipartFormDataContent())
                {
                    var stream = new StreamContent(mediaFile.Contents.ToStream());
                    stream.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mediaFile.MimeType)
                        ? "application/octet-stream"
                        : mediaFile.MimeType);

                    form.Add(stream, "data_file", string.IsNullOrWhiteSpace(mediaFile.Filename) ? "input.bin" : mediaFile.Filename);

                    var configJson = BuildConfig(language);
                    form.Add(new StringContent(configJson, Encoding.UTF8, MimeTypes.Json), "config");

                    using var createResp = await client.PostAsync($"{BaseUrl}/jobs", form, cancellationToken);
                    createJobJson = await createResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!createResp.IsSuccessStatusCode)
                        throw new Exception($"{createResp.StatusCode}: {createJobJson}");

                    jobId = ExtractJobId(createJobJson);
                    if (string.IsNullOrWhiteSpace(jobId))
                        throw new Exception("Speechmatics did not return a job id.");
                }

                // 2) Poll job details
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                        throw new TimeoutException($"Speechmatics transcription timed out after {maxWaitSeconds}s.");

                    cancellationToken.ThrowIfCancellationRequested();

                    using var detailsResp = await client.GetAsync($"{BaseUrl}/jobs/{Uri.EscapeDataString(jobId!)}", cancellationToken);
                    var detailsJson = await detailsResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!detailsResp.IsSuccessStatusCode)
                        throw new Exception($"{detailsResp.StatusCode}: {detailsJson}");

                    finalJobJson = detailsJson;
                    var status = ExtractStatus(detailsJson);

                    if (IsDoneStatus(status))
                        break;

                    if (IsErrorStatus(status))
                        throw new Exception($"Speechmatics job failed with status '{status ?? "unknown"}'.");

                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                }

                // 3) Fetch transcript
                using (var transcriptResp = await client.GetAsync($"{BaseUrl}/jobs/{Uri.EscapeDataString(jobId!)}/transcript", cancellationToken))
                {
                    transcriptRaw = await transcriptResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!transcriptResp.IsSuccessStatusCode)
                        throw new Exception($"{transcriptResp.StatusCode}: {transcriptRaw}");
                }

                var transcriptText = ExtractTranscriptText(transcriptRaw!);
                if (string.IsNullOrWhiteSpace(transcriptText))
                    transcriptText = transcriptRaw!;

                var safeName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();

                var uploadedTxt = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.txt",
                    BinaryData.FromString(transcriptText),
                    cancellationToken);

                var rawPayload = JsonSerializer.Serialize(new
                {
                    provider = "speechmatics",
                    baseUrl = BaseUrl,
                    fileUrl,
                    language,
                    jobId,
                    createJob = TryParseJsonNode(createJobJson),
                    finalJobDetails = TryParseJsonNode(finalJobJson),
                    transcript = TryParseJsonNode(transcriptRaw)
                });

                var uploadedJson = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.json",
                    BinaryData.FromString(rawPayload),
                    cancellationToken);

                return new
                {
                    provider = "speechmatics",
                    modelType = "transcription",
                    fileUrl,
                    language,
                    jobId,
                    transcript = transcriptText,
                    output = new
                    {
                        transcriptFileUri = uploadedTxt?.Uri,
                        transcriptFileName = uploadedTxt?.Name,
                        transcriptMimeType = uploadedTxt?.MimeType,
                        rawResponseFileUri = uploadedJson?.Uri,
                        rawResponseFileName = uploadedJson?.Name,
                        rawResponseMimeType = uploadedJson?.MimeType
                    }
                };
            }));

    private static string BuildConfig(string? language)
    {
        var config = new Dictionary<string, object?>
        {
            ["type"] = "transcription",
            ["transcription_config"] = string.IsNullOrWhiteSpace(language)
                ? []
                : new Dictionary<string, object?> { ["language"] = language }
        };

        return JsonSerializer.Serialize(config);
    }

    private static string? ExtractJobId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            return id.GetString();

        if (root.TryGetProperty("job", out var job)
            && job.TryGetProperty("id", out var nestedId)
            && nestedId.ValueKind == JsonValueKind.String)
            return nestedId.GetString();

        return null;
    }

    private static string? ExtractStatus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            return status.GetString();

        if (root.TryGetProperty("job", out var job)
            && job.TryGetProperty("status", out var nested)
            && nested.ValueKind == JsonValueKind.String)
            return nested.GetString();

        return null;
    }

    private static bool IsDoneStatus(string? status)
        => string.Equals(status, "done", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase);

    private static bool IsErrorStatus(string? status)
        => string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "expired", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractTranscriptText(string transcriptRaw)
    {
        try
        {
            using var doc = JsonDocument.Parse(transcriptRaw);
            var root = doc.RootElement;

            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                return text.GetString();

            if (root.TryGetProperty("transcript", out var transcript) && transcript.ValueKind == JsonValueKind.String)
                return transcript.GetString();

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                var lines = new List<string>();
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("alternatives", out var alternatives)
                        && alternatives.ValueKind == JsonValueKind.Array
                        && alternatives.GetArrayLength() > 0)
                    {
                        var first = alternatives[0];
                        if (first.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                            lines.Add(content.GetString()!);
                    }
                }

                if (lines.Count > 0)
                    return string.Join(" ", lines);
            }
        }
        catch
        {
            // Response may already be plain text.
        }

        return transcriptRaw;
    }

    private static object? TryParseJsonNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(json);
        }
        catch
        {
            return json;
        }
    }
}

