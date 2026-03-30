using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Lumenfall;

public static class LumenfallVideo
{
    private const int DefaultPollIntervalSeconds = 3;
    private const int DefaultMaxWaitSeconds = 900;

    [Description("Generate video with Lumenfall, always confirm via elicitation, optionally pass a single input fileUrl for image-to-video, poll until completed, upload resulting video(s) to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Lumenfall Video Generate", Name = "lumenfall_video_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Lumenfall_Video_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video model name.")] string model,
        [Description("Prompt describing the requested video.")] string prompt,
        [Description("Optional single input image URL (SharePoint/OneDrive/public HTTP).")]
        string? fileUrl = null,
        [Description("Optional video duration in seconds.")] string? seconds = null,
        [Description("Optional output size (for example 1920x1080 or 16:9).")]
        string? size = null,
        [Description("Number of videos to generate (1-4).")][Range(1, 4)] int n = 1,
        [Description("Optional aspect ratio.")] string? aspect_ratio = null,
        [Description("Optional resolution shorthand (for example 720p or 1080p).")]
        string? resolution = null,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Optional media retention policy.")] string? media_retention = null,
        [Description("Optional webhook URL.")] string? webhook_url = null,
        [Description("Optional idempotency key.")] string? idempotency_key = null,
        [Description("Optional metadata as JSON object string.")] string? metadataJson = null,
        [Description("Optional end-user identifier.")] string? user = null,
        [Description("If true, only returns cost estimate.")] bool dryRun = false,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LumenfallVideoGenerateRequest
                {
                    Model = model,
                    Prompt = prompt,
                    FileUrl = fileUrl,
                    Seconds = seconds,
                    Size = size,
                    N = n,
                    AspectRatio = aspect_ratio,
                    Resolution = resolution,
                    NegativePrompt = negative_prompt,
                    MediaRetention = media_retention,
                    WebhookUrl = webhook_url,
                    IdempotencyKey = idempotency_key,
                    MetadataJson = metadataJson,
                    User = user,
                    DryRun = dryRun,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerate(typed);

            var body = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["n"] = typed.N
            };

            AddOptional(body, "seconds", typed.Seconds);
            AddOptional(body, "size", typed.Size);
            AddOptional(body, "aspect_ratio", typed.AspectRatio);
            AddOptional(body, "resolution", typed.Resolution);
            AddOptional(body, "negative_prompt", typed.NegativePrompt);
            AddOptional(body, "media_retention", typed.MediaRetention);
            AddOptional(body, "webhook_url", typed.WebhookUrl);
            AddOptional(body, "idempotency_key", typed.IdempotencyKey);
            AddOptional(body, "user", typed.User);

            if (!string.IsNullOrWhiteSpace(typed.MetadataJson))
            {
                var parsedMetadata = JsonNode.Parse(typed.MetadataJson);
                if (parsedMetadata is JsonObject metadataObj)
                    body["metadata"] = metadataObj;
            }

            if (!string.IsNullOrWhiteSpace(typed.FileUrl))
            {
                var sourceImageUrl = await GetModelAccessibleUrlFromFileUrlAsync(
                    serviceProvider,
                    requestContext,
                    typed.FileUrl,
                    cancellationToken);

                body["input_reference"] = new JsonObject
                {
                    ["image_url"] = sourceImageUrl
                };
            }

            var client = serviceProvider.GetRequiredService<LumenfallClient>();
            var createPath = typed.DryRun ? "videos?dryRun=true" : "videos";
            var created = await client.PostJsonAsync(createPath, body, cancellationToken)
                ?? throw new Exception("Lumenfall did not return a response body.");

            if (typed.DryRun)
                return created.ToJsonString().ToJsonCallToolResponse("https://api.lumenfall.ai/openai/v1/videos?dryRun=true");

            var videoId = created["id"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(videoId))
                throw new Exception($"Lumenfall did not return video id. Response: {created}");

            var completed = await PollUntilTerminalAsync(
                client,
                videoId,
                typed.PollIntervalSeconds,
                typed.MaxWaitSeconds,
                cancellationToken);

            var status = completed["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            if (status == "failed")
            {
                var error = completed["error"]?.ToJsonString() ?? "unknown error";
                throw new Exception($"Lumenfall video {videoId} failed: {error}");
            }

            var outputUrls = ExtractOutputUrls(completed);
            if (outputUrls.Count == 0)
                throw new Exception($"Lumenfall video {videoId} completed but no output URL(s) were returned.");

            var links = await DownloadUploadFromUrlsAsync(
                serviceProvider,
                requestContext,
                outputUrls,
                typed.Filename,
                cancellationToken);

            if (links.Count == 0)
                throw new Exception($"Lumenfall video {videoId} completed but no output video(s) could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Get Lumenfall video status by id. If already completed, uploads output video(s) to SharePoint/OneDrive and returns resource link blocks.")]
    [McpServerTool(Title = "Lumenfall Video Get", Name = "lumenfall_video_get", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Lumenfall_Video_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Lumenfall video id.")] string id,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LumenfallVideoGetRequest
                {
                    Id = id,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (string.IsNullOrWhiteSpace(typed.Id))
                throw new ValidationException("id is required.");

            var client = serviceProvider.GetRequiredService<LumenfallClient>();
            var video = await client.GetAsync($"videos/{Uri.EscapeDataString(typed.Id)}", cancellationToken)
                ?? throw new Exception("Lumenfall video retrieval returned empty response.");

            var status = video["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            if (status != "completed")
                return video.ToJsonString().ToJsonCallToolResponse($"https://api.lumenfall.ai/openai/v1/videos/{typed.Id}");

            var outputUrls = ExtractOutputUrls(video);
            if (outputUrls.Count == 0)
                throw new Exception($"Lumenfall video {typed.Id} is completed but no output URL(s) were found.");

            var links = await DownloadUploadFromUrlsAsync(
                serviceProvider,
                requestContext,
                outputUrls,
                typed.Filename,
                cancellationToken);

            if (links.Count == 0)
                throw new Exception($"Lumenfall video {typed.Id} is completed but output video(s) could not be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Cancel a Lumenfall video request by id.")]
    [McpServerTool(Title = "Lumenfall Video Cancel", Name = "lumenfall_video_cancel", Destructive = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Lumenfall_Video_Cancel(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Lumenfall video id.")] string id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LumenfallVideoCancelRequest
                {
                    Id = id
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (string.IsNullOrWhiteSpace(typed.Id))
                throw new ValidationException("id is required.");

            var client = serviceProvider.GetRequiredService<LumenfallClient>();
            await client.DeleteAsync($"videos/{Uri.EscapeDataString(typed.Id)}", cancellationToken);

            var payload = new JsonObject
            {
                ["id"] = typed.Id,
                ["cancelled"] = true
            };

            return payload.ToJsonString().ToJsonCallToolResponse($"https://api.lumenfall.ai/openai/v1/videos/{typed.Id}");
        });

    private static async Task<JsonNode> PollUntilTerminalAsync(
        LumenfallClient client,
        string videoId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            var status = await client.GetAsync($"videos/{Uri.EscapeDataString(videoId)}", timeoutCts.Token)
                ?? throw new Exception($"Lumenfall polling returned empty response for video {videoId}.");

            var state = status["status"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? string.Empty;
            if (state is "completed" or "failed")
                return status;

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"Lumenfall video {videoId} did not complete within {maxWaitSeconds} seconds.");
    }

    private static async Task<string> GetModelAccessibleUrlFromFileUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source file from fileUrl.");

        var sourceFilename = string.IsNullOrWhiteSpace(file.Filename)
            ? $"lumenfall-video-source{GetVideoExtension(null, file.MimeType)}"
            : file.Filename;

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            sourceFilename,
            BinaryData.FromBytes(file.Contents.ToArray()),
            cancellationToken);

        if (uploaded == null || string.IsNullOrWhiteSpace(uploaded.Uri))
            throw new Exception("Failed to upload source file for Lumenfall processing.");

        return uploaded.Uri;
    }

    private static async Task<List<ResourceLinkBlock>> DownloadUploadFromUrlsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        IEnumerable<string> urls,
        string? filename,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var links = new List<ResourceLinkBlock>();
        var baseName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp4");
        var i = 0;

        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            i++;
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            var ext = GetVideoExtension(file.Filename, file.MimeType);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{baseName}-{i}{ext}",
                BinaryData.FromBytes(file.Contents.ToArray()),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        return links;
    }

    private static List<string> ExtractOutputUrls(JsonNode root)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectUrlsFromNode(root["output"], urls);
        CollectUrlsFromNode(root["data"], urls);
        if (urls.Count == 0)
            CollectUrlsFromNode(root, urls);

        return [.. urls];
    }

    private static void CollectUrlsFromNode(JsonNode? node, HashSet<string> urls)
    {
        if (node == null)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    var key = kv.Key;
                    var value = kv.Value;
                    if (value == null)
                        continue;

                    if (value is JsonValue jv
                        && jv.TryGetValue<string>(out var s)
                        && !string.IsNullOrWhiteSpace(s)
                        && IsOutputUrlKey(key)
                        && Uri.TryCreate(s, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        urls.Add(s);
                    }

                    CollectUrlsFromNode(value, urls);
                }

                break;

            case JsonArray arr:
                foreach (var child in arr)
                    CollectUrlsFromNode(child, urls);

                break;
        }
    }

    private static bool IsOutputUrlKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized is "image_url" or "input_url" or "fileurl")
            return false;

        return normalized.Contains("url", StringComparison.Ordinal)
               && (normalized.Contains("video", StringComparison.Ordinal)
                   || normalized is "url" or "download_url" or "output_url" or "file_uri" or "fileuri");
    }

    private static void AddOptional(JsonObject body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            body[key] = value;
    }

    private static void ValidateGenerate(LumenfallVideoGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (input.N is < 1 or > 4)
            throw new ValidationException("n must be between 1 and 4.");

        if (input.PollIntervalSeconds is < 1 or > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (input.MaxWaitSeconds is < 30 or > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    private static string GetVideoExtension(string? filename, string? mimeType)
    {
        var ext = Path.GetExtension(filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "video/quicktime" => ".mov",
            "video/x-msvideo" => ".avi",
            "video/webm" => ".webm",
            _ => ".mp4"
        };
    }
}

[Description("Please confirm the Lumenfall video generation request details.")]
public sealed class LumenfallVideoGenerateRequest
{
    [JsonPropertyName("model")]
    [Required]
    [Description("Video model name.")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text for generation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("fileUrl")]
    [Description("Optional single input image URL (SharePoint/OneDrive/public HTTP).")]
    public string? FileUrl { get; set; }

    [JsonPropertyName("seconds")]
    [Description("Optional video duration in seconds.")]
    public string? Seconds { get; set; }

    [JsonPropertyName("size")]
    [Description("Optional output size.")]
    public string? Size { get; set; }

    [JsonPropertyName("n")]
    [Range(1, 4)]
    [Description("Number of videos to generate (1-4).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("resolution")]
    [Description("Optional resolution shorthand.")]
    public string? Resolution { get; set; }

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("media_retention")]
    [Description("Optional media retention policy.")]
    public string? MediaRetention { get; set; }

    [JsonPropertyName("webhook_url")]
    [Description("Optional webhook URL.")]
    public string? WebhookUrl { get; set; }

    [JsonPropertyName("idempotency_key")]
    [Description("Optional idempotency key.")]
    public string? IdempotencyKey { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Optional metadata as JSON object string.")]
    public string? MetadataJson { get; set; }

    [JsonPropertyName("user")]
    [Description("Optional end-user identifier.")]
    public string? User { get; set; }

    [JsonPropertyName("dryRun")]
    [Description("If true, only returns cost estimate.")]
    public bool DryRun { get; set; }

    [JsonPropertyName("pollIntervalSeconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollIntervalSeconds { get; set; } = 3;

    [JsonPropertyName("maxWaitSeconds")]
    [Range(30, 3600)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = 900;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Lumenfall video get request details.")]
public sealed class LumenfallVideoGetRequest
{
    [JsonPropertyName("id")]
    [Required]
    [Description("Lumenfall video id.")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Lumenfall video cancel request details.")]
public sealed class LumenfallVideoCancelRequest
{
    [JsonPropertyName("id")]
    [Required]
    [Description("Lumenfall video id.")]
    public string Id { get; set; } = default!;
}

