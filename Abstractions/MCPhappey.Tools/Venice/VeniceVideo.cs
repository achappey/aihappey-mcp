using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

namespace MCPhappey.Tools.Venice;

public static class VeniceVideo
{
    private const string QuotePath = "video/quote";
    private const string QueuePath = "video/queue";
    private const string RetrievePath = "video/retrieve";
    private const int DefaultPollIntervalSeconds = 3;
    private const int DefaultMaxWaitSeconds = 600;

    [Description("Quote Venice AI video generation pricing for a model configuration.")]
    [McpServerTool(Title = "Venice video quote", Name = "venice_video_quote", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Video_Quote(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Model ID for the quote request.")] string model,
        [Description("Duration of the video. Allowed values: 5s, 10s.")] string duration,
        [Description("Optional aspect ratio, for example: 16:9.")] string? aspect_ratio = null,
        [Description("Optional resolution. Allowed values: 1080p, 720p, 480p.")] string? resolution = "720p",
        [Description("Optional audio toggle for supported models. Default: true.")] bool audio = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceVideoQuoteRequest
                {
                    Model = NormalizeRequired(model, "model"),
                    Duration = NormalizeDuration(duration),
                    AspectRatio = NormalizeOptional(aspect_ratio),
                    Resolution = NormalizeResolution(resolution),
                    Audio = audio
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var body = new JsonObject
            {
                ["model"] = typed.Model,
                ["duration"] = typed.Duration,
                ["resolution"] = typed.Resolution,
                ["audio"] = typed.Audio
            };
            AddIfNotNull(body, "aspect_ratio", typed.AspectRatio);

            using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, QuotePath)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Venice video quote failed ({(int)resp.StatusCode}): {raw}");

            var parsed = JsonNode.Parse(raw)?.AsObject()
                         ?? throw new InvalidOperationException("Venice video quote returned invalid JSON.");

            return new CallToolResult
            {
                StructuredContent = new JsonObject
                {
                    ["provider"] = "venice",
                    ["type"] = "video_quote",
                    ["model"] = typed.Model,
                    ["duration"] = typed.Duration,
                    ["aspect_ratio"] = typed.AspectRatio,
                    ["resolution"] = typed.Resolution,
                    ["audio"] = typed.Audio,
                    ["quote"] = parsed["quote"]
                },
                Content =
                [
                    parsed.ToJsonString().ToTextContentBlock()
                ]
            };
        });

    [Description("Generate a Venice AI text-to-video, upload resulting video(s) to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Venice text-to-video", Name = "venice_video_text_to_video", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Video_TextToVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt text describing the video to generate.")] string prompt,
        [Description("Model ID for text-to-video.")] string model = "wan-2.5-preview-text-to-video",
        [Description("Duration of the video. Allowed values: 5s, 10s.")] string duration = "5s",
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Optional aspect ratio, for example: 16:9.")] string? aspect_ratio = null,
        [Description("Optional resolution. Allowed values: 1080p, 720p, 480p.")] string? resolution = "720p",
        [Description("Optional audio toggle for supported models. Default: true.")] bool audio = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceVideoTextToVideoRequest
                {
                    Prompt = NormalizeRequired(prompt, "prompt"),
                    Model = NormalizeRequired(model, "model"),
                    Duration = NormalizeDuration(duration),
                    NegativePrompt = NormalizeOptional(negative_prompt),
                    AspectRatio = NormalizeOptional(aspect_ratio),
                    Resolution = NormalizeResolution(resolution),
                    Audio = audio,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var queueBody = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["duration"] = typed.Duration,
                ["resolution"] = typed.Resolution,
                ["audio"] = typed.Audio
            };
            AddIfNotNull(queueBody, "negative_prompt", typed.NegativePrompt);
            AddIfNotNull(queueBody, "aspect_ratio", typed.AspectRatio);

            var (queuedModel, queueId) = await QueueVideoAsync(serviceProvider, queueBody, cancellationToken);
            var videoBytes = await PollUntilCompleteAsync(serviceProvider, queuedModel, queueId, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.mp4",
                BinaryData.FromBytes(videoBytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Generate a Venice AI image-to-video from one fileUrl input, upload resulting video(s) to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Venice image-to-video", Name = "venice_video_image_to_video", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Video_ImageToVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("Prompt text describing the video to generate.")] string prompt,
        [Description("Model ID for image-to-video.")] string model = "wan-2.5-preview-image-to-video",
        [Description("Duration of the video. Allowed values: 5s, 10s.")] string duration = "5s",
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Optional aspect ratio, for example: 16:9.")] string? aspect_ratio = null,
        [Description("Optional resolution. Allowed values: 1080p, 720p, 480p.")] string? resolution = "720p",
        [Description("Optional audio toggle for supported models. Default: true.")] bool audio = true,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceVideoImageToVideoRequest
                {
                    FileUrl = NormalizeRequired(fileUrl, "fileUrl"),
                    Prompt = NormalizeRequired(prompt, "prompt"),
                    Model = NormalizeRequired(model, "model"),
                    Duration = NormalizeDuration(duration),
                    NegativePrompt = NormalizeOptional(negative_prompt),
                    AspectRatio = NormalizeOptional(aspect_ratio),
                    Resolution = NormalizeResolution(resolution),
                    Audio = audio,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            var imageDataUrl = await DownloadImageAsDataUrlAsync(serviceProvider, requestContext, typed.FileUrl, cancellationToken);

            var queueBody = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["duration"] = typed.Duration,
                ["image_url"] = imageDataUrl,
                ["resolution"] = typed.Resolution,
                ["audio"] = typed.Audio
            };
            AddIfNotNull(queueBody, "negative_prompt", typed.NegativePrompt);
            AddIfNotNull(queueBody, "aspect_ratio", typed.AspectRatio);

            var (queuedModel, queueId) = await QueueVideoAsync(serviceProvider, queueBody, cancellationToken);
            var videoBytes = await PollUntilCompleteAsync(serviceProvider, queuedModel, queueId, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.mp4",
                BinaryData.FromBytes(videoBytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static async Task<(string Model, string QueueId)> QueueVideoAsync(
        IServiceProvider serviceProvider,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
        using var req = new HttpRequestMessage(HttpMethod.Post, QueuePath)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Venice video queue failed ({(int)resp.StatusCode}): {raw}");

        var parsed = JsonNode.Parse(raw)?.AsObject()
                     ?? throw new InvalidOperationException("Venice video queue returned invalid JSON.");

        var model = parsed["model"]?.GetValue<string>();
        var queueId = parsed["queue_id"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(queueId))
            throw new InvalidOperationException("Venice video queue did not return model and queue_id.");

        return (model, queueId);
    }

    private static async Task<byte[]> PollUntilCompleteAsync(
        IServiceProvider serviceProvider,
        string model,
        string queueId,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultMaxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            var retrieval = await RetrieveVideoAsync(serviceProvider, model, queueId, timeoutCts.Token);
            if (retrieval.VideoBytes is not null)
                return retrieval.VideoBytes;

            if (!string.Equals(retrieval.Status, "PROCESSING", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Venice video retrieve returned unexpected status '{retrieval.Status}'.");

            await Task.Delay(TimeSpan.FromSeconds(DefaultPollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"Venice video generation did not complete within {DefaultMaxWaitSeconds} seconds.");
    }

    private static async Task<RetrieveResult> RetrieveVideoAsync(
        IServiceProvider serviceProvider,
        string model,
        string queueId,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["queue_id"] = queueId,
            ["delete_media_on_completion"] = false
        };

        using var client = serviceProvider.CreateVeniceClient("*/*");
        using var req = new HttpRequestMessage(HttpMethod.Post, RetrievePath)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var rawError = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Venice video retrieve failed ({(int)resp.StatusCode}): {rawError}");
        }

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            if (bytes.Length == 0)
                throw new InvalidOperationException("Venice video retrieve returned empty video data.");

            return new RetrieveResult { VideoBytes = bytes };
        }

        var raw = Encoding.UTF8.GetString(bytes);
        var parsed = JsonNode.Parse(raw)?.AsObject()
                     ?? throw new InvalidOperationException("Venice video retrieve returned invalid JSON status payload.");
        var status = parsed["status"]?.GetValue<string>() ?? "UNKNOWN";

        return new RetrieveResult
        {
            Status = status
        };
    }

    private static async Task<string> DownloadImageAsDataUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image content from fileUrl.");

        var mime = string.IsNullOrWhiteSpace(file.MimeType) ? "image/png" : file.MimeType;
        var base64 = Convert.ToBase64String(file.Contents.ToArray());
        return $"data:{mime};base64,{base64}";
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{field} is required.");

        return value.Trim();
    }

    private static string NormalizeDuration(string? value)
    {
        var normalized = NormalizeRequired(value ?? "5s", "duration").ToLowerInvariant();
        return normalized is "5s" or "10s"
            ? normalized
            : throw new ValidationException("duration must be one of: 5s, 10s.");
    }

    private static string NormalizeResolution(string? value)
    {
        var normalized = NormalizeRequired(value ?? "720p", "resolution").ToLowerInvariant();
        return normalized is "1080p" or "720p" or "480p"
            ? normalized
            : throw new ValidationException("resolution must be one of: 1080p, 720p, 480p.");
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfNotNull<T>(JsonObject node, string propertyName, T? value)
    {
        if (value is null)
            return;

        node[propertyName] = JsonValue.Create(value);
    }

    private sealed class RetrieveResult
    {
        public string? Status { get; init; }
        public byte[]? VideoBytes { get; init; }
    }
}

[Description("Please confirm the Venice video quote request.")]
public sealed class VeniceVideoQuoteRequest
{
    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID for quote.")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("duration")]
    [Required]
    [Description("Duration of the video. Allowed values: 5s, 10s.")]
    public string Duration { get; set; } = "5s";

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("resolution")]
    [Description("Resolution. Allowed values: 1080p, 720p, 480p.")]
    public string Resolution { get; set; } = "720p";

    [JsonPropertyName("audio")]
    [Description("Enable or disable audio for supported models.")]
    public bool Audio { get; set; } = true;
}

[Description("Please confirm the Venice text-to-video request.")]
public sealed class VeniceVideoTextToVideoRequest
{
    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID for text-to-video.")]
    public string Model { get; set; } = "wan-2.5-preview-text-to-video";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing the video to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("duration")]
    [Required]
    [Description("Duration of the video. Allowed values: 5s, 10s.")]
    public string Duration { get; set; } = "5s";

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("resolution")]
    [Description("Resolution. Allowed values: 1080p, 720p, 480p.")]
    public string Resolution { get; set; } = "720p";

    [JsonPropertyName("audio")]
    [Description("Enable or disable audio for supported models.")]
    public bool Audio { get; set; } = true;

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Venice image-to-video request.")]
public sealed class VeniceVideoImageToVideoRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID for image-to-video.")]
    public string Model { get; set; } = "wan-2.5-preview-image-to-video";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing the video to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("duration")]
    [Required]
    [Description("Duration of the video. Allowed values: 5s, 10s.")]
    public string Duration { get; set; } = "5s";

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("resolution")]
    [Description("Resolution. Allowed values: 1080p, 720p, 480p.")]
    public string Resolution { get; set; } = "720p";

    [JsonPropertyName("audio")]
    [Description("Enable or disable audio for supported models.")]
    public bool Audio { get; set; } = true;

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}
