using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Supadata;

public static class SupadataYouTube
{
    [Description("Translate a YouTube transcript to another language.")]
    [McpServerTool(
        Title = "Supadata YouTube transcript translate",
        Name = "supadata_youtube_transcript_translate",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Supadata_YouTube_Transcript_Translate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Language code for translation (ISO 639-1).")]
        string lang,
        [Description("YouTube video URL.")]
        string? url = null,
        [Description("YouTube video ID.")]
        string? videoId = null,
        [Description("When true, returns plain text transcript.")]
        bool? text = null,
        [Description("Maximum characters per transcript chunk (only when text=false).")]
        int? chunkSize = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SupadataYouTubeTranscriptTranslateRequest
                {
                    Lang = lang,
                    Url = url,
                    VideoId = videoId,
                    Text = text,
                    ChunkSize = chunkSize
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(typed.Url) && string.IsNullOrWhiteSpace(typed.VideoId))
                throw new ValidationException("Provide url or videoId.");

            var query = BuildQuery(
                ("lang", typed.Lang),
                ("url", typed.Url),
                ("videoId", typed.VideoId),
                ("text", typed.Text?.ToString()?.ToLowerInvariant()),
                ("chunkSize", typed.ChunkSize?.ToString()));

            var client = serviceProvider.GetRequiredService<SupadataClient>();
            var result = await client.GetAsync($"youtube/transcript/translate{query}", cancellationToken);
            return result;
        }));

    private static string BuildQuery(params (string Key, string? Value)[] items)
    {
        var query = string.Join("&", items
            .Where(i => !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => $"{Uri.EscapeDataString(i.Key)}={Uri.EscapeDataString(i.Value!)}"));

        return string.IsNullOrWhiteSpace(query) ? string.Empty : $"?{query}";
    }
}

public sealed class SupadataYouTubeTranscriptTranslateRequest
{
    [JsonPropertyName("lang")]
    [Required]
    [Description("Language code for translation (ISO 639-1).")]
    public string Lang { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    [Description("YouTube video URL.")]
    public string? Url { get; set; }

    [JsonPropertyName("videoId")]
    [Description("YouTube video ID.")]
    public string? VideoId { get; set; }

    [JsonPropertyName("text")]
    [Description("When true, returns plain text transcript.")]
    public bool? Text { get; set; }

    [JsonPropertyName("chunkSize")]
    [Description("Maximum characters per transcript chunk (only when text=false).")]
    public int? ChunkSize { get; set; }
}
