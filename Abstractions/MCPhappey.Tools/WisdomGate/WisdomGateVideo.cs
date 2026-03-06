using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WisdomGate;

public static class WisdomGateVideo
{
    private const int DefaultPollIntervalSeconds = 10;
    private const int DefaultMaxWaitSeconds = 600;

    [Description("Generate a video with Wisdom Gate, wait for completion, upload the resulting file, and return a resource link block.")]
    [McpServerTool(
        Title = "Wisdom Gate Generate Video",
        Name = "wisdomgate_video_generate",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WisdomGate_Video_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the video to generate.")] string prompt,
        [Description("Video model ID. One of: sora-2, sora-2-pro, veo-3.1.")] string model = "sora-2",
        [Description("Duration in seconds. Supported values include: 4, 8, 10, 12, 15, 25.")] int seconds = 4,
        [Description("Resolution (widthxheight). One of: 720x1280, 1280x720, 1024x1792, 1792x1024.")] string size = "720x1280",
        [Description("Optional reference image file URL. Supports SharePoint and OneDrive secured links.")] string? fileUrl = null,
        [Description("Polling interval in seconds.")] [Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")] [Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var input = new WisdomGateVideoGenerateRequest
            {
                Prompt = prompt,
                Model = model,
                Seconds = seconds,
                Size = size,
                FileUrl = fileUrl,
                PollIntervalSeconds = pollIntervalSeconds,
                MaxWaitSeconds = maxWaitSeconds,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp4")
            };

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateVideoRequest(typed);

            using var client = serviceProvider.CreateWisdomGateClient();

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(typed.Prompt), "prompt");
            form.Add(new StringContent(typed.Model), "model");
            form.Add(new StringContent(typed.Seconds.ToString()), "seconds");
            form.Add(new StringContent(typed.Size), "size");

            if (!string.IsNullOrWhiteSpace(typed.FileUrl))
            {
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
                var source = files.FirstOrDefault() ?? throw new Exception("Failed to download fileUrl content.");

                var contentType = string.IsNullOrWhiteSpace(source.MimeType) ? "application/octet-stream" : source.MimeType!;
                var imageContent = new ByteArrayContent(source.Contents.ToArray());
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);

                var sourceName = string.IsNullOrWhiteSpace(source.Filename) ? "input_reference.png" : source.Filename;
                form.Add(imageContent, "input_reference", sourceName);
            }

            using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/v1/videos") { Content = form };
            using var submitResp = await client.SendAsync(submitReq, cancellationToken);
            var submitJson = await submitResp.Content.ReadAsStringAsync(cancellationToken);

            if (!submitResp.IsSuccessStatusCode)
                throw new Exception($"Wisdom Gate video submit failed ({submitResp.StatusCode}): {submitJson}");

            var videoId = ExtractVideoId(submitJson);
            await WaitForCompletionAsync(client, videoId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);

            var downloadBytes = await DownloadVideoContentAsync(client, videoId, cancellationToken);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.mp4",
                BinaryData.FromBytes(downloadBytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static string ExtractVideoId(string submitJson)
    {
        using var doc = JsonDocument.Parse(submitJson);
        if (!doc.RootElement.TryGetProperty("id", out var idEl))
            throw new Exception("Wisdom Gate did not return a video id.");

        var id = idEl.GetString();
        if (string.IsNullOrWhiteSpace(id))
            throw new Exception("Wisdom Gate returned an empty video id.");

        return id;
    }

    private static async Task WaitForCompletionAsync(
        HttpClient client,
        string videoId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/videos/{Uri.EscapeDataString(videoId)}");
            using var resp = await client.SendAsync(req, timeoutCts.Token);
            var statusJson = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Wisdom Gate status polling failed ({resp.StatusCode}): {statusJson}");

            using var doc = JsonDocument.Parse(statusJson);
            var status = doc.RootElement.TryGetProperty("status", out var statusEl)
                ? statusEl.GetString()?.Trim().ToLowerInvariant()
                : null;

            if (status == "completed")
                return;

            if (status == "failed")
            {
                var error = doc.RootElement.TryGetProperty("error", out var errorEl) ? errorEl.ToString() : "unknown error";
                throw new Exception($"Wisdom Gate video generation failed: {error}");
            }

            if (status != "queued" && status != "processing")
                throw new Exception($"Wisdom Gate returned unsupported status '{status ?? "null"}'.");

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"Wisdom Gate video generation did not complete within {maxWaitSeconds} seconds.");
    }

    private static async Task<byte[]> DownloadVideoContentAsync(HttpClient client, string videoId, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/videos/{Uri.EscapeDataString(videoId)}/content");
        using var resp = await client.SendAsync(req, cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var errorJson = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Wisdom Gate video download failed ({resp.StatusCode}): {errorJson}");
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("video", StringComparison.OrdinalIgnoreCase))
        {
            var errorJson = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Wisdom Gate video content endpoint returned non-video payload: {errorJson}");
        }

        return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void ValidateVideoRequest(WisdomGateVideoGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        var allowedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sora-2", "sora-2-pro", "veo-3.1"
        };
        if (!allowedModels.Contains(input.Model))
            throw new ValidationException("model must be one of: sora-2, sora-2-pro, veo-3.1.");

        var allowedSeconds = new HashSet<int> { 4, 8, 10, 12, 15, 25 };
        if (!allowedSeconds.Contains(input.Seconds))
            throw new ValidationException("seconds must be one of: 4, 8, 10, 12, 15, 25.");

        var allowedSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "720x1280", "1280x720", "1024x1792", "1792x1024"
        };
        if (!allowedSizes.Contains(input.Size))
            throw new ValidationException("size must be one of: 720x1280, 1280x720, 1024x1792, 1792x1024.");

        if (input.PollIntervalSeconds < 1 || input.PollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (input.MaxWaitSeconds < 30 || input.MaxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    [Description("Please confirm the Wisdom Gate video generation request details.")]
    public sealed class WisdomGateVideoGenerateRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Video model ID.")]
        public string Model { get; set; } = "sora-2";

        [JsonPropertyName("seconds")]
        [Required]
        [Description("Duration in seconds.")]
        public int Seconds { get; set; } = 4;

        [JsonPropertyName("size")]
        [Required]
        [Description("Resolution (widthxheight).")]
        public string Size { get; set; } = "720x1280";

        [JsonPropertyName("fileUrl")]
        [Description("Optional reference image file URL.")]
        public string? FileUrl { get; set; }

        [JsonPropertyName("pollIntervalSeconds")]
        [Range(1, 60)]
        [Description("Polling interval in seconds.")]
        public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

        [JsonPropertyName("maxWaitSeconds")]
        [Range(30, 3600)]
        [Description("Maximum wait time in seconds.")]
        public int MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename base.")]
        public string Filename { get; set; } = default!;
    }
}

