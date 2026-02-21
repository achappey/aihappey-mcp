using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.RekaAI.Vision;

public static class RekaVision
{
    private const string BaseUrl = "https://vision-agent.api.reka.ai";

    [Description("Quick-tag a short video (under 30 seconds) from a file URL and return structured tags. SharePoint/OneDrive/HTTPS URLs are supported.")]
    [McpServerTool(
        Title = "Reka Vision Quick Tag Video",
        Name = "reka_vision_quick_tag_video",
        OpenWorld = true,
        ReadOnly = true,
        Destructive = false)]
    public static async Task<CallToolResult?> RekaVision_QuickTagVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video URL to read and quick-tag (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileUrl))
                    throw new ArgumentException("fileUrl is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var file = files.FirstOrDefault() ?? throw new Exception("Unable to download the provided video URL.");

                using var form = new MultipartFormDataContent();
                var bytes = file.Contents.ToArray();
                using var videoContent = new ByteArrayContent(bytes);
                videoContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
                form.Add(videoContent, "video", string.IsNullOrWhiteSpace(file.Filename) ? "video.mp4" : file.Filename);

                var (jsonNode, jsonText) = await SendRequestAsync(
                    clientFactory,
                    settings.ApiKey,
                    HttpMethod.Post,
                    $"{BaseUrl}/v1/qa/quicktag",
                    form,
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = jsonNode,
                    Content = [
                        "Video quick-tag completed successfully.".ToTextContentBlock(),
                        jsonText.ToTextContentBlock()
                    ]
                };
            }));

    [Description("Upload a video to Reka Vision indexing backend using a PUBLICLY accessible URL only. Local/private URLs are rejected. Uses elicitation confirmation before upload.")]
    [McpServerTool(
        Title = "Reka Vision Upload Video",
        Name = "reka_vision_upload_video",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> RekaVision_UploadVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Public video URL only (http/https). This tool does NOT support local file upload.")]
        string videoUrl,
        [Description("Whether to index the uploaded video. Default true.")]
        bool index = true,
        [Description("Enable thumbnails when indexing.")]
        bool? enableThumbnails = null,
        [Description("Optional display name for the video.")]
        string? videoName = null,
        [Description("Optional ISO8601 timestamp when recording started.")]
        string? videoAbsoluteStartTimestamp = null,
        [Description("Optional JSON string with advanced VideoIndexingParams config.")]
        string? config = null,
        [Description("Enable person/object indexing.")]
        bool? personIndexing = null,
        [Description("Persist extracted frames for retrieval.")]
        bool? persistFrames = null,
        [Description("Optional custom caption prompt.")]
        string? captionPrompt = null,
        [Description("Encode chunks during indexing.")]
        bool? encodeChunks = null,
        [Description("Optional caption mode: generic, security, tagging_ad_video, tte_1110.")]
        string? captionMode = null,
        [Description("Optional group ID for the uploaded video.")]
        string? groupId = null,
        [Description("Optional JSON string containing custom chunking config.")]
        string? chunkingConfig = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                EnsurePublicUrl(videoUrl, "videoUrl");

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new RekaVisionUploadVideoInput
                    {
                        VideoUrl = videoUrl,
                        Index = index,
                        EnableThumbnails = enableThumbnails,
                        VideoName = videoName,
                        VideoAbsoluteStartTimestamp = videoAbsoluteStartTimestamp,
                        Config = config,
                        PersonIndexing = personIndexing,
                        PersistFrames = persistFrames,
                        CaptionPrompt = captionPrompt,
                        EncodeChunks = encodeChunks,
                        CaptionMode = captionMode,
                        GroupId = groupId,
                        ChunkingConfig = chunkingConfig,
                        Confirmation = "UPLOAD"
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "No input data provided".ToErrorCallToolResponse();

                EnsurePublicUrl(typed.VideoUrl, "videoUrl");

                if (!string.Equals(typed.Confirmation?.Trim(), "UPLOAD", StringComparison.OrdinalIgnoreCase))
                    return "Upload canceled: confirmation text must be 'UPLOAD'.".ToErrorCallToolResponse();

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                using var form = new MultipartFormDataContent
                {
                    { new StringContent(typed.VideoUrl), "video_url" },
                    { new StringContent(typed.Index ? "true" : "false"), "index" }
                };

                AddString(form, "video_name", typed.VideoName);
                AddString(form, "video_absolute_start_timestamp", typed.VideoAbsoluteStartTimestamp);
                AddString(form, "config", typed.Config);
                AddString(form, "caption_prompt", typed.CaptionPrompt);
                AddString(form, "caption_mode", typed.CaptionMode);
                AddString(form, "group_id", typed.GroupId);
                AddString(form, "chunking_config", typed.ChunkingConfig);
                AddBool(form, "enable_thumbnails", typed.EnableThumbnails);
                AddBool(form, "person_indexing", typed.PersonIndexing);
                AddBool(form, "persist_frames", typed.PersistFrames);
                AddBool(form, "encode_chunks", typed.EncodeChunks);

                var (jsonNode, jsonText) = await SendRequestAsync(
                    clientFactory,
                    settings.ApiKey,
                    HttpMethod.Post,
                    $"{BaseUrl}/v1/videos/upload",
                    form,
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = jsonNode,
                    Content = [
                        "Video upload request accepted by Reka Vision.".ToTextContentBlock(),
                        jsonText.ToTextContentBlock()
                    ]
                };
            }));

    [Description("Delete a video by ID from Reka Vision. Uses default confirmation flow before deletion.")]
    [McpServerTool(
        Title = "Reka Vision Delete Video",
        Name = "reka_vision_delete_video",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> RekaVision_DeleteVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video ID to delete.")]
        string videoId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(videoId))
                    throw new ArgumentException("videoId is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteVideo>(
                    videoId,
                    async ct =>
                    {
                        using var client = clientFactory.CreateClient();
                        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/v1/videos/{Uri.EscapeDataString(videoId)}");
                        req.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);

                        using var resp = await client.SendAsync(req, ct);
                        var json = await resp.Content.ReadAsStringAsync(ct);
                        if (!resp.IsSuccessStatusCode)
                            throw new Exception($"{resp.StatusCode}: {json}");
                    },
                    $"Video '{videoId}' deleted successfully.",
                    cancellationToken);
            }));

    [Description("Upload an image to Reka Vision indexing backend using a PUBLICLY accessible URL only. Local/private URLs are rejected. Uses elicitation confirmation before upload.")]
    [McpServerTool(
        Title = "Reka Vision Upload Image",
        Name = "reka_vision_upload_image",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> RekaVision_UploadImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Public image URL only (http/https). This tool does NOT support local file upload.")]
        string imageUrl,
        [Description("Metadata JSON string (required by API). Example: [{\"caption\":\"camera frame\"}]")]
        string metadata,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                EnsurePublicUrl(imageUrl, "imageUrl");

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new RekaVisionUploadImageInput
                    {
                        ImageUrl = imageUrl,
                        Metadata = metadata,
                        Confirmation = "UPLOAD"
                    },
                    cancellationToken);

                if (notAccepted != null) return notAccepted;
                if (typed == null) return "No input data provided".ToErrorCallToolResponse();

                EnsurePublicUrl(typed.ImageUrl, "imageUrl");

                if (!string.Equals(typed.Confirmation?.Trim(), "UPLOAD", StringComparison.OrdinalIgnoreCase))
                    return "Upload canceled: confirmation text must be 'UPLOAD'.".ToErrorCallToolResponse();

                if (string.IsNullOrWhiteSpace(typed.Metadata))
                    return "metadata is required for image upload.".ToErrorCallToolResponse();

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                using var form = new MultipartFormDataContent
                {
                    { new StringContent(JsonSerializer.Serialize(new[] { typed.ImageUrl })), "image_urls" },
                    { new StringContent(typed.Metadata, Encoding.UTF8, "application/json"), "metadata" }
                };

                var (jsonNode, jsonText) = await SendRequestAsync(
                    clientFactory,
                    settings.ApiKey,
                    HttpMethod.Post,
                    $"{BaseUrl}/v1/images/upload",
                    form,
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = jsonNode,
                    Content = [
                        "Image upload request accepted by Reka Vision.".ToTextContentBlock(),
                        jsonText.ToTextContentBlock()
                    ]
                };
            }));

    [Description("Delete an image by ID from Reka Vision. Uses default confirmation flow before deletion.")]
    [McpServerTool(
        Title = "Reka Vision Delete Image",
        Name = "reka_vision_delete_image",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> RekaVision_DeleteImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Image ID to delete.")]
        string imageId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(imageId))
                    throw new ArgumentException("imageId is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteImage>(
                    imageId,
                    async ct =>
                    {
                        using var client = clientFactory.CreateClient();
                        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/v1/images/{Uri.EscapeDataString(imageId)}");
                        req.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);

                        using var resp = await client.SendAsync(req, ct);
                        var json = await resp.Content.ReadAsStringAsync(ct);
                        if (!resp.IsSuccessStatusCode)
                            throw new Exception($"{resp.StatusCode}: {json}");
                    },
                    $"Image '{imageId}' deleted successfully.",
                    cancellationToken);
            }));

    [Description("Search indexed images with plaintext query in Reka Vision.")]
    [McpServerTool(
        Title = "Reka Vision Search Images",
        Name = "reka_vision_search_images",
        OpenWorld = true,
        ReadOnly = true,
        Destructive = false)]
    public static async Task<CallToolResult?> RekaVision_SearchImages(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query text.")]
        string query,
        [Description("Maximum results to return. Default 10.")]
        int maxResults = 10,
        [Description("Search mode: vision or joined.")]
        string? searchMode = null,
        [Description("Optional image weight (joined mode only).")]
        double? imageWeight = null,
        [Description("Optional text weight (joined mode only).")]
        double? textWeight = null,
        [Description("Optional similarity score threshold.")]
        double? threshold = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("query is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_results"] = maxResults
                };

                if (!string.IsNullOrWhiteSpace(searchMode)) payload["search_mode"] = searchMode;
                if (imageWeight.HasValue) payload["image_weight"] = imageWeight.Value;
                if (textWeight.HasValue) payload["text_weight"] = textWeight.Value;
                if (threshold.HasValue) payload["threshold"] = threshold.Value;

                using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                var (jsonNode, jsonText) = await SendRequestAsync(
                    clientFactory,
                    settings.ApiKey,
                    HttpMethod.Post,
                    $"{BaseUrl}/v1/images/search",
                    content,
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = jsonNode,
                    Content = [
                        "Image search completed.".ToTextContentBlock(),
                        jsonText.ToTextContentBlock()
                    ]
                };
            }));

    [Description("Search indexed videos with embedding-based query in Reka Vision.")]
    [McpServerTool(
        Title = "Reka Vision Search Videos",
        Name = "reka_vision_search_videos",
        OpenWorld = true,
        ReadOnly = true,
        Destructive = false)]
    public static async Task<CallToolResult?> RekaVision_SearchVideos(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query text.")]
        string query,
        [Description("Maximum results to return. Default 10.")]
        int maxResults = 10,
        [Description("Optional threshold.")]
        double? threshold = null,
        [Description("Optional video IDs as comma-separated text OR JSON array string.")]
        string? videoIds = null,
        [Description("Optional group IDs as comma-separated text OR JSON array string.")]
        string? groupIds = null,
        [Description("Include demo videos in search.")]
        bool? searchDemo = null,
        [Description("Enable LLM rerank.")]
        bool? useLlmRerank = null,
        [Description("Enable embeddings rerank.")]
        bool? useEmbedsRerank = null,
        [Description("Add OCR text to captions.")]
        bool? addOcrToCaption = null,
        [Description("Filter from datetime (ISO8601).")]
        string? datetimeFrom = null,
        [Description("Filter to datetime (ISO8601).")]
        string? datetimeTo = null,
        [Description("Filter from timestamp in seconds.")]
        double? timestampFrom = null,
        [Description("Filter to timestamp in seconds.")]
        double? timestampTo = null,
        [Description("Generate explanatory report.")]
        bool? generateReport = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("query is required.");

                var settings = serviceProvider.GetRequiredService<RekaAISettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_results"] = maxResults
                };

                if (threshold.HasValue) payload["threshold"] = threshold.Value;
                if (searchDemo.HasValue) payload["search_demo"] = searchDemo.Value;
                if (useLlmRerank.HasValue) payload["use_llm_rerank"] = useLlmRerank.Value;
                if (useEmbedsRerank.HasValue) payload["use_embeds_rerank"] = useEmbedsRerank.Value;
                if (addOcrToCaption.HasValue) payload["add_ocr_to_caption"] = addOcrToCaption.Value;
                if (!string.IsNullOrWhiteSpace(datetimeFrom)) payload["datetime_from"] = datetimeFrom;
                if (!string.IsNullOrWhiteSpace(datetimeTo)) payload["datetime_to"] = datetimeTo;
                if (timestampFrom.HasValue) payload["timestamp_from"] = timestampFrom.Value;
                if (timestampTo.HasValue) payload["timestamp_to"] = timestampTo.Value;
                if (generateReport.HasValue) payload["generate_report"] = generateReport.Value;

                var parsedVideoIds = ParseStringArray(videoIds);
                if (parsedVideoIds?.Count > 0)
                {
                    var arr = new JsonArray();
                    foreach (var id in parsedVideoIds) arr.Add(id);
                    payload["video_ids"] = arr;
                }

                var parsedGroupIds = ParseStringArray(groupIds);
                if (parsedGroupIds?.Count > 0)
                {
                    var arr = new JsonArray();
                    foreach (var id in parsedGroupIds) arr.Add(id);
                    payload["group_ids"] = arr;
                }

                using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                var (jsonNode, jsonText) = await SendRequestAsync(
                    clientFactory,
                    settings.ApiKey,
                    HttpMethod.Post,
                    $"{BaseUrl}/v1/videos/search",
                    content,
                    cancellationToken);

                return new CallToolResult
                {
                    StructuredContent = jsonNode,
                    Content = [
                        "Video search completed.".ToTextContentBlock(),
                        jsonText.ToTextContentBlock()
                    ]
                };
            }));

    private static void AddString(MultipartFormDataContent form, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            form.Add(new StringContent(value), field);
    }

    private static void AddBool(MultipartFormDataContent form, string field, bool? value)
    {
        if (value.HasValue)
            form.Add(new StringContent(value.Value ? "true" : "false"), field);
    }

    private static void EnsurePublicUrl(string input, string parameterName)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
            throw new ArgumentException($"{parameterName} must be an absolute URL.");

        if (!(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"{parameterName} must use http or https.");

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException($"{parameterName} host is invalid.");

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.IsLoopback)
            throw new ArgumentException($"{parameterName} must be publicly accessible. Local hosts are not allowed.");

        if (IPAddress.TryParse(host, out var ip) && IsPrivateIp(ip))
            throw new ArgumentException($"{parameterName} must be publicly accessible. Private IPs are not allowed.");
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();

        // IPv4 private/link-local ranges.
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return (bytes[0] == 10)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 127);
        }

        // IPv6 loopback/link-local/unique local.
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal || ip.IsIPv6Teredo || ip.Equals(IPAddress.IPv6Loopback);
        }

        return false;
    }

    private static List<string>? ParseStringArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        raw = raw.Trim();

        if (raw.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(raw);
                return parsed?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch
            {
                // Fall back to split parsing.
            }
        }

        return raw
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<(JsonObject jsonNode, string jsonText)> SendRequestAsync(
        IHttpClientFactory clientFactory,
        string apiKey,
        HttpMethod method,
        string url,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var client = clientFactory.CreateClient();
        using var req = new HttpRequestMessage(method, url)
        {
            Content = content
        };
        req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

        using var resp = await client.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {json}");

        var node = JsonNode.Parse(json)?.AsObject() ?? [];
        return (node, json);
    }

    [Description("Please review and confirm the Reka Vision video upload request. Type UPLOAD to continue.")]
    public class RekaVisionUploadVideoInput
    {
        [JsonPropertyName("videoUrl")]
        [Required]
        [Description("PUBLIC video URL to upload (http/https).")]
        public string VideoUrl { get; set; } = default!;

        [JsonPropertyName("index")]
        [Required]
        [Description("Whether to index the video.")]
        public bool Index { get; set; } = true;

        [JsonPropertyName("enableThumbnails")]
        [Description("Enable thumbnail extraction when indexing.")]
        public bool? EnableThumbnails { get; set; }

        [JsonPropertyName("videoName")]
        [Description("Optional name for the video.")]
        public string? VideoName { get; set; }

        [JsonPropertyName("videoAbsoluteStartTimestamp")]
        [Description("Optional ISO8601 recording start timestamp.")]
        public string? VideoAbsoluteStartTimestamp { get; set; }

        [JsonPropertyName("config")]
        [Description("Optional JSON string for advanced config.")]
        public string? Config { get; set; }

        [JsonPropertyName("personIndexing")]
        [Description("Enable person/object indexing.")]
        public bool? PersonIndexing { get; set; }

        [JsonPropertyName("persistFrames")]
        [Description("Persist extracted frames.")]
        public bool? PersistFrames { get; set; }

        [JsonPropertyName("captionPrompt")]
        [Description("Optional caption prompt.")]
        public string? CaptionPrompt { get; set; }

        [JsonPropertyName("encodeChunks")]
        [Description("Encode chunks during indexing.")]
        public bool? EncodeChunks { get; set; }

        [JsonPropertyName("captionMode")]
        [Description("Optional caption mode.")]
        public string? CaptionMode { get; set; }

        [JsonPropertyName("groupId")]
        [Description("Optional group ID.")]
        public string? GroupId { get; set; }

        [JsonPropertyName("chunkingConfig")]
        [Description("Optional JSON string for chunking config.")]
        public string? ChunkingConfig { get; set; }

        [JsonPropertyName("confirmation")]
        [Required]
        [Description("Type UPLOAD to confirm this upload.")]
        public string Confirmation { get; set; } = "UPLOAD";
    }

    [Description("Please review and confirm the Reka Vision image upload request. Type UPLOAD to continue.")]
    public class RekaVisionUploadImageInput
    {
        [JsonPropertyName("imageUrl")]
        [Required]
        [Description("PUBLIC image URL to upload (http/https).")]
        public string ImageUrl { get; set; } = default!;

        [JsonPropertyName("metadata")]
        [Required]
        [Description("Metadata JSON string.")]
        public string Metadata { get; set; } = default!;

        [JsonPropertyName("confirmation")]
        [Required]
        [Description("Type UPLOAD to confirm this upload.")]
        public string Confirmation { get; set; } = "UPLOAD";
    }

    [Description("Please confirm deletion of the video ID: {0}")]
    public class ConfirmDeleteVideo : IHasName
    {
        [Required]
        [Description("Enter the exact video ID to confirm deletion: {0}")]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm deletion of the image ID: {0}")]
    public class ConfirmDeleteImage : IHasName
    {
        [Required]
        [Description("Enter the exact image ID to confirm deletion: {0}")]
        public string Name { get; set; } = default!;
    }
}

