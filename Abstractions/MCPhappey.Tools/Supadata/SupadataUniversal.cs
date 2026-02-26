using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Supadata;

public static class SupadataUniversal
{
    private const int DefaultPollIntervalSeconds = 2;

    [Description("Extract structured data from a supported video URL using Supadata.")]
    [McpServerTool(
        Title = "Supadata extract",
        Name = "supadata_extract",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Supadata_Extract(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video URL (YouTube, TikTok, Instagram, Twitter/X, Facebook).")]
        string url,
        [Description("Extraction prompt describing what to extract.")]
        string? prompt = null,
        [Description("JSON Schema describing the extraction result.")]
        JsonNode? schema = null,
        [Description("If true, poll until the job is completed. Default: true.")]
        bool waitUntilCompleted = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SupadataExtractRequest
                {
                    Url = url,
                    Prompt = prompt,
                    Schema = schema,
                    WaitUntilCompleted = waitUntilCompleted
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Url);
            if (string.IsNullOrWhiteSpace(typed.Prompt) && typed.Schema == null)
                throw new ValidationException("Either prompt or schema must be provided.");

            var client = serviceProvider.GetRequiredService<SupadataClient>();

            var payload = new
            {
                url = typed.Url,
                prompt = string.IsNullOrWhiteSpace(typed.Prompt) ? null : typed.Prompt,
                schema = typed.Schema
            };

            var created = await client.PostAsync("extract", payload, cancellationToken);
            if (!typed.WaitUntilCompleted)
                return created;

            var jobId = created?["jobId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(jobId))
                throw new Exception($"Supadata extract did not return jobId. Response: {created}");

            var result = await SupadataPolling.PollUntilCompletedAsync(
                ct => client.GetAsync($"extract/{Uri.EscapeDataString(jobId)}", ct),
                node => node?["status"]?.GetValue<string>(),
                node => node?["error"]?.ToJsonString(),
                DefaultPollIntervalSeconds,
                cancellationToken);

            return result;
        }));

    [Description("Create a batch job to fetch metadata for multiple YouTube videos. Always confirms parameters, waits for completion, and returns results.")]
    [McpServerTool(
        Title = "Supadata YouTube video batch",
        Name = "supadata_youtube_video_batch",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Supadata_YouTube_Video_Batch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Array of YouTube video IDs or URLs.")]
        string[]? videoIds = null,
        [Description("YouTube playlist URL or ID.")]
        string? playlistId = null,
        [Description("YouTube channel URL, handle, or ID.")]
        string? channelId = null,
        [Description("Max number of videos to process when using playlistId/channelId.")]
        int? limit = null,
        [Description("If true, poll until the job is completed. Default: true.")]
        bool waitUntilCompleted = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SupadataYouTubeBatchRequest
                {
                    VideoIds = videoIds,
                    PlaylistId = playlistId,
                    ChannelId = channelId,
                    Limit = limit,
                    WaitUntilCompleted = waitUntilCompleted
                },
                cancellationToken);

            if ((typed.VideoIds == null || typed.VideoIds.Length == 0)
                && string.IsNullOrWhiteSpace(typed.PlaylistId)
                && string.IsNullOrWhiteSpace(typed.ChannelId))
            {
                throw new ValidationException("Provide videoIds, playlistId, or channelId.");
            }

            var client = serviceProvider.GetRequiredService<SupadataClient>();
            var payload = new
            {
                videoIds = typed.VideoIds?.Length > 0 ? typed.VideoIds : null,
                playlistId = string.IsNullOrWhiteSpace(typed.PlaylistId) ? null : typed.PlaylistId,
                channelId = string.IsNullOrWhiteSpace(typed.ChannelId) ? null : typed.ChannelId,
                limit = typed.Limit
            };

            var created = await client.PostAsync("youtube/video/batch", payload, cancellationToken);
            if (!typed.WaitUntilCompleted)
                return created;

            var jobId = created?["jobId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(jobId))
                throw new Exception($"Supadata YouTube video batch did not return jobId. Response: {created}");

            var result = await SupadataPolling.PollUntilCompletedAsync(
                ct => client.GetAsync($"youtube/batch/{Uri.EscapeDataString(jobId)}", ct),
                node => node?["status"]?.GetValue<string>(),
                node => node?["errorCode"]?.GetValue<string>(),
                DefaultPollIntervalSeconds,
                cancellationToken);

            return result;
        }));

    [Description("Create a batch job to fetch transcripts for multiple YouTube videos.")]
    [McpServerTool(
        Title = "Supadata YouTube transcript batch",
        Name = "supadata_youtube_transcript_batch",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Supadata_YouTube_Transcript_Batch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Array of YouTube video IDs or URLs.")]
        string[]? videoIds = null,
        [Description("YouTube playlist URL or ID.")]
        string? playlistId = null,
        [Description("YouTube channel URL, handle, or ID.")]
        string? channelId = null,
        [Description("Max number of videos to process when using playlistId/channelId.")]
        int? limit = null,
        [Description("Preferred transcript language code (ISO 639-1).")]
        string? lang = null,
        [Description("When true, returns plain text transcript.")]
        bool? text = null,
        [Description("If true, poll until the job is completed. Default: true.")]
        bool waitUntilCompleted = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SupadataYouTubeTranscriptBatchRequest
                {
                    VideoIds = videoIds,
                    PlaylistId = playlistId,
                    ChannelId = channelId,
                    Limit = limit,
                    Lang = lang,
                    Text = text,
                    WaitUntilCompleted = waitUntilCompleted
                },
                cancellationToken);

            if ((typed.VideoIds == null || typed.VideoIds.Length == 0)
                && string.IsNullOrWhiteSpace(typed.PlaylistId)
                && string.IsNullOrWhiteSpace(typed.ChannelId))
            {
                throw new ValidationException("Provide videoIds, playlistId, or channelId.");
            }

            var client = serviceProvider.GetRequiredService<SupadataClient>();
            var payload = new
            {
                videoIds = typed.VideoIds?.Length > 0 ? typed.VideoIds : null,
                playlistId = string.IsNullOrWhiteSpace(typed.PlaylistId) ? null : typed.PlaylistId,
                channelId = string.IsNullOrWhiteSpace(typed.ChannelId) ? null : typed.ChannelId,
                limit = typed.Limit,
                lang = string.IsNullOrWhiteSpace(typed.Lang) ? null : typed.Lang,
                text = typed.Text
            };

            var created = await client.PostAsync("youtube/transcript/batch", payload, cancellationToken);
            if (!typed.WaitUntilCompleted)
                return created;

            var jobId = created?["jobId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(jobId))
                throw new Exception($"Supadata YouTube transcript batch did not return jobId. Response: {created}");

            var result = await SupadataPolling.PollUntilCompletedAsync(
                ct => client.GetAsync($"youtube/batch/{Uri.EscapeDataString(jobId)}", ct),
                node => node?["status"]?.GetValue<string>(),
                node => node?["errorCode"]?.GetValue<string>(),
                DefaultPollIntervalSeconds,
                cancellationToken);

            return result;
        }));
}

public sealed class SupadataExtractRequest
{
    [JsonPropertyName("url")]
    [Required]
    [Description("Video URL to extract from.")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    [Description("Description of what to extract.")]
    public string? Prompt { get; set; }

    [JsonPropertyName("schema")]
    [Description("JSON Schema for extraction output.")]
    public JsonNode? Schema { get; set; }

    [JsonPropertyName("waitUntilCompleted")]
    [DefaultValue(true)]
    [Description("If true, poll until the job completes.")]
    public bool WaitUntilCompleted { get; set; } = true;


}

public sealed class SupadataYouTubeBatchRequest
{
    [JsonPropertyName("videoIds")]
    [Description("Array of YouTube video IDs or URLs.")]
    public string[]? VideoIds { get; set; }

    [JsonPropertyName("playlistId")]
    [Description("YouTube playlist URL or ID.")]
    public string? PlaylistId { get; set; }

    [JsonPropertyName("channelId")]
    [Description("YouTube channel URL, handle, or ID.")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("limit")]
    [Description("Maximum number of videos to process.")]
    public int? Limit { get; set; }

    [JsonPropertyName("waitUntilCompleted")]
    [DefaultValue(true)]
    [Description("If true, poll until the job completes.")]
    public bool WaitUntilCompleted { get; set; } = true;
}

public sealed class SupadataYouTubeTranscriptBatchRequest
{
    [JsonPropertyName("videoIds")]
    [Description("Array of YouTube video IDs or URLs.")]
    public string[]? VideoIds { get; set; }

    [JsonPropertyName("playlistId")]
    [Description("YouTube playlist URL or ID.")]
    public string? PlaylistId { get; set; }

    [JsonPropertyName("channelId")]
    [Description("YouTube channel URL, handle, or ID.")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("limit")]
    [Description("Maximum number of videos to process.")]
    public int? Limit { get; set; }

    [JsonPropertyName("lang")]
    [Description("Preferred transcript language code (ISO 639-1).")]
    public string? Lang { get; set; }

    [JsonPropertyName("text")]
    [Description("When true, returns plain text transcript.")]
    public bool? Text { get; set; }

    [JsonPropertyName("waitUntilCompleted")]
    [DefaultValue(true)]
    [Description("If true, poll until the job completes.")]
    public bool WaitUntilCompleted { get; set; } = true;
}
