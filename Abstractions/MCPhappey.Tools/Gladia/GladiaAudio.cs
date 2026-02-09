using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Gladia;

public static class GladiaAudio
{
    private const string UploadUrl = "https://api.gladia.io/v2/upload";
    private const string PreRecordedUrl = "https://api.gladia.io/v2/pre-recorded";

    [Description("Transcribe pre-recorded audio/video using Gladia speech-to-text.")]
    [McpServerTool(
        Title = "Gladia Speech-to-Text",
        Name = "gladia_audio_transcribe_audio",
        Destructive = false)]
    public static async Task<CallToolResult?> GladiaAudio_TranscribeAudio(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Audio/video file URL to transcribe (supports SharePoint/OneDrive links).")]
        string fileUrl,
        [Description("Language hint (ISO-639-1), e.g. en or nl. Ignored when detectLanguage=true.")]
        string language = "en",
        [Description("Auto detect language.")]
        bool detectLanguage = false,
        [Description("Enable code switching across utterances.")]
        bool codeSwitching = false,
        [Description("Enable speaker diarization.")]
        bool diarization = false,
        [Description("Minimum speaker count for diarization (0 = provider default).")]
        int minSpeakers = 0,
        [Description("Maximum speaker count for diarization (0 = provider default).")]
        int maxSpeakers = 0,
        [Description("Polling interval in seconds.")]
        int pollingIntervalSeconds = 2,
        [Description("Maximum wait time in seconds before timeout.")]
        int maxWaitSeconds = 900,
        [Description("Output filename without extension.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<GladiaSettings>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            if (string.IsNullOrWhiteSpace(fileUrl))
                throw new ArgumentException("fileUrl is required.");

            pollingIntervalSeconds = Math.Max(1, pollingIntervalSeconds);
            maxWaitSeconds = Math.Max(30, maxWaitSeconds);

            // Validate the URL is downloadable in this MCP context (OneDrive/SharePoint/HTTP)
            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            _ = downloads.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download audio or video content.");

            using var client = clientFactory.CreateClient();

            string? transcriptionId = null;
            string? finalResultJson = null;

            try
            {
                // 1) Upload via URL for stable provider-side fetch
                var uploadPayload = JsonSerializer.Serialize(new { audio_url = fileUrl });
                using var uploadReq = CreateRequest(HttpMethod.Post, UploadUrl, settings.ApiKey,
                    new StringContent(uploadPayload, Encoding.UTF8, MimeTypes.Json));

                using var uploadResp = await client.SendAsync(uploadReq, cancellationToken);
                var uploadJson = await uploadResp.Content.ReadAsStringAsync(cancellationToken);

                if (!uploadResp.IsSuccessStatusCode)
                    throw new Exception($"{uploadResp.StatusCode}: {uploadJson}");

                var uploadedAudioUrl = ExtractJsonString(uploadJson, "audio_url");
                if (string.IsNullOrWhiteSpace(uploadedAudioUrl))
                    throw new Exception("Gladia did not return audio_url after upload.");

                // 2) Initiate pre-recorded transcription
                var initPayload = BuildInitPayload(uploadedAudioUrl!, language, detectLanguage, codeSwitching, diarization, minSpeakers, maxSpeakers);
                using var initReq = CreateRequest(HttpMethod.Post, PreRecordedUrl, settings.ApiKey,
                    new StringContent(initPayload.ToJsonString(), Encoding.UTF8, MimeTypes.Json));

                using var initResp = await client.SendAsync(initReq, cancellationToken);
                var initJson = await initResp.Content.ReadAsStringAsync(cancellationToken);

                if (!initResp.IsSuccessStatusCode)
                    throw new Exception($"{initResp.StatusCode}: {initJson}");

                transcriptionId = ExtractJsonString(initJson, "id");
                if (string.IsNullOrWhiteSpace(transcriptionId))
                    throw new Exception("Gladia did not return a transcription job id.");

                // 3) Poll until completed
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                        throw new TimeoutException($"Gladia transcription timed out after {maxWaitSeconds}s.");

                    cancellationToken.ThrowIfCancellationRequested();

                    using var statusReq = CreateRequest(HttpMethod.Get, $"{PreRecordedUrl}/{Uri.EscapeDataString(transcriptionId)}", settings.ApiKey);
                    using var statusResp = await client.SendAsync(statusReq, cancellationToken);
                    var statusJson = await statusResp.Content.ReadAsStringAsync(cancellationToken);

                    if (!statusResp.IsSuccessStatusCode)
                        throw new Exception($"{statusResp.StatusCode}: {statusJson}");

                    var status = ExtractJsonString(statusJson, "status");

                    if (string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
                    {
                        finalResultJson = statusJson;
                        break;
                    }

                    if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorCode = ExtractJsonString(statusJson, "error_code");
                        throw new Exception($"Gladia transcription failed. Status=error, ErrorCode={errorCode ?? "unknown"}.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), cancellationToken);
                }

                // 4) Extract transcript and upload artifacts
                var transcript = ExtractTranscript(finalResultJson!);
                if (string.IsNullOrWhiteSpace(transcript))
                    transcript = "No transcript text found in Gladia response.";

                var safeName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName();

                var uploadedTxt = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.txt",
                    BinaryData.FromString(transcript),
                    cancellationToken);

                var uploadedJson = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{safeName}.json",
                    BinaryData.FromString(finalResultJson!),
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
            }
            finally
            {
                // Best-effort cleanup: remove transcription + associated data after retrieval/failure.
                if (!string.IsNullOrWhiteSpace(transcriptionId))
                {
                    try
                    {
                        using var deleteReq = CreateRequest(HttpMethod.Delete,
                            $"{PreRecordedUrl}/{Uri.EscapeDataString(transcriptionId)}", settings.ApiKey);

                        using var _ = await client.SendAsync(deleteReq, cancellationToken);
                    }
                    catch
                    {
                        // Intentionally ignored: cleanup is best-effort.
                    }
                }
            }
        });

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("x-gladia-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        if (content != null)
            request.Content = content;
        return request;
    }

    private static JsonObject BuildInitPayload(
        string uploadedAudioUrl,
        string language,
        bool detectLanguage,
        bool codeSwitching,
        bool diarization,
        int minSpeakers,
        int maxSpeakers)
    {
        var payload = new JsonObject
        {
            ["audio_url"] = uploadedAudioUrl,
            ["diarization"] = diarization,
            ["sentences"] = true,
        };

        if (diarization)
        {
            var diarizationConfig = new JsonObject();
            if (minSpeakers > 0) diarizationConfig["min_speakers"] = minSpeakers;
            if (maxSpeakers > 0) diarizationConfig["max_speakers"] = maxSpeakers;
            if (diarizationConfig.Count > 0)
                payload["diarization_config"] = diarizationConfig;
        }

        var languageConfig = new JsonObject();
        if (!detectLanguage && !string.IsNullOrWhiteSpace(language))
            languageConfig["languages"] = new JsonArray(language);
        if (codeSwitching)
            languageConfig["code_switching"] = true;

        if (languageConfig.Count > 0)
            payload["language_config"] = languageConfig;

        return payload;
    }

    private static string? ExtractJsonString(string json, params string[] path)
    {
        using var doc = JsonDocument.Parse(json);
        var current = doc.RootElement;

        foreach (var part in path)
        {
            if (!current.TryGetProperty(part, out var next))
                return null;
            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    private static string? ExtractTranscript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("result", out var result)
            && result.TryGetProperty("transcription", out var transcription)
            && transcription.TryGetProperty("full_transcript", out var fullTranscript)
            && fullTranscript.ValueKind == JsonValueKind.String)
        {
            return fullTranscript.GetString();
        }

        if (root.TryGetProperty("result", out result)
            && result.TryGetProperty("transcription", out transcription)
            && transcription.TryGetProperty("utterances", out var utterances)
            && utterances.ValueKind == JsonValueKind.Array)
        {
            var lines = utterances
                .EnumerateArray()
                .Select(u => u.TryGetProperty("text", out var t) ? t.GetString() : null)
                .Where(t => !string.IsNullOrWhiteSpace(t));

            return string.Join(Environment.NewLine, lines!);
        }

        return null;
    }
}

