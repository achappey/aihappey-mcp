using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DumplingAI;

public static class DumplingAIData
{
    [Description("Get detailed YouTube channel metadata using a channel ID, handle, or full channel URL.")]
    [McpServerTool(Title = "DumplingAI get YouTube channel", Name = "dumplingai_get_youtube_channel", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeChannel(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube channel ID.")] string? channelId = null,
        [Description("Optional YouTube handle, usually starting with @.")] string? handle = null,
        [Description("Optional full YouTube channel URL.")] string? url = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-channel",
            new JsonObject
            {
                ["channelId"] = channelId,
                ["handle"] = handle,
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube channel lookup completed.",
            () => EnsureAnyProvided("channelId, handle, or url", channelId, handle, url));

    [Description("List uploaded long-form videos for a YouTube channel.")]
    [McpServerTool(Title = "DumplingAI get YouTube channel videos", Name = "dumplingai_get_youtube_channel_videos", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeChannelVideos(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube channel ID.")] string? channelId = null,
        [Description("Optional YouTube handle, usually starting with @.")] string? handle = null,
        [Description("Optional full YouTube channel URL.")] string? url = null,
        [Description("Optional pagination cursor or continuation token.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-channel-videos",
            new JsonObject
            {
                ["channelId"] = channelId,
                ["handle"] = handle,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube channel videos lookup completed.",
            () => EnsureAnyProvided("channelId, handle, or url", channelId, handle, url));

    [Description("List YouTube Shorts for a channel.")]
    [McpServerTool(Title = "DumplingAI get YouTube channel shorts", Name = "dumplingai_get_youtube_channel_shorts", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeChannelShorts(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube channel ID.")] string? channelId = null,
        [Description("Optional YouTube handle, usually starting with @.")] string? handle = null,
        [Description("Optional full YouTube channel URL.")] string? url = null,
        [Description("Optional pagination cursor or continuation token.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-channel-shorts",
            new JsonObject
            {
                ["channelId"] = channelId,
                ["handle"] = handle,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube channel shorts lookup completed.",
            () => EnsureAnyProvided("channelId, handle, or url", channelId, handle, url));

    [Description("Get detailed metadata for a YouTube video or short.")]
    [McpServerTool(Title = "DumplingAI get YouTube video", Name = "dumplingai_get_youtube_video", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube video ID.")] string? videoId = null,
        [Description("Optional full YouTube video URL.")] string? url = null,
        [Description("Include transcript data when available.")] bool? includeTranscript = null,
        [Description("Optional preferred transcript language, such as en.")] string? preferredLanguage = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-video",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["includeTranscript"] = includeTranscript,
                ["preferredLanguage"] = preferredLanguage,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube video lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("Get public comments for a YouTube video.")]
    [McpServerTool(Title = "DumplingAI get YouTube video comments", Name = "dumplingai_get_youtube_video_comments", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeVideoComments(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube video ID.")] string? videoId = null,
        [Description("Optional full YouTube video URL.")] string? url = null,
        [Description("Optional comment sort mode, for example top or newest.")] string? sort = null,
        [Description("Optional pagination cursor or continuation token.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-video-comments",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["sort"] = sort,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube video comments lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("Search YouTube for videos, shorts, or channels.")]
    [McpServerTool(Title = "DumplingAI search YouTube", Name = "dumplingai_search_youtube", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchYouTube(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The search query.")] string query,
        [Description("Optional search type, such as video, short, or channel.")] string? type = null,
        [Description("Optional pagination cursor or continuation token.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search-youtube",
            new JsonObject
            {
                ["query"] = query,
                ["type"] = type,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Get transcript segments for a YouTube video.")]
    [McpServerTool(Title = "DumplingAI get YouTube transcript", Name = "dumplingai_get_youtube_transcript", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetYouTubeTranscript(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional YouTube video ID.")] string? videoId = null,
        [Description("Optional full YouTube video URL.")] string? url = null,
        [Description("Optional preferred transcript language, such as en.")] string? preferredLanguage = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-youtube-transcript",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["preferredLanguage"] = preferredLanguage,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI YouTube transcript lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("Get public profile metadata for a TikTok user.")]
    [McpServerTool(Title = "DumplingAI get TikTok profile", Name = "dumplingai_get_tiktok_profile", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokProfile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok username or handle.")] string? username = null,
        [Description("Optional full TikTok profile URL.")] string? url = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-profile",
            new JsonObject
            {
                ["username"] = username,
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok profile lookup completed.",
            () => EnsureAnyProvided("username or url", username, url));

    [Description("List videos published by a TikTok profile.")]
    [McpServerTool(Title = "DumplingAI get TikTok profile videos", Name = "dumplingai_get_tiktok_profile_videos", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokProfileVideos(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok username or handle.")] string? username = null,
        [Description("Optional full TikTok profile URL.")] string? url = null,
        [Description("Optional pagination cursor.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-profile-videos",
            new JsonObject
            {
                ["username"] = username,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok profile videos lookup completed.",
            () => EnsureAnyProvided("username or url", username, url));

    [Description("Get detailed metadata for a TikTok video.")]
    [McpServerTool(Title = "DumplingAI get TikTok video", Name = "dumplingai_get_tiktok_video", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok video ID.")] string? videoId = null,
        [Description("Optional full TikTok video URL.")] string? url = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-video",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok video lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("Get public comments for a TikTok video.")]
    [McpServerTool(Title = "DumplingAI get TikTok video comments", Name = "dumplingai_get_tiktok_video_comments", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokVideoComments(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok video ID.")] string? videoId = null,
        [Description("Optional full TikTok video URL.")] string? url = null,
        [Description("Optional pagination cursor.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-video-comments",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok video comments lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("List followers for a TikTok profile.")]
    [McpServerTool(Title = "DumplingAI get TikTok user followers", Name = "dumplingai_get_tiktok_user_followers", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokUserFollowers(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok username or handle.")] string? username = null,
        [Description("Optional full TikTok profile URL.")] string? url = null,
        [Description("Optional pagination cursor.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-user-followers",
            new JsonObject
            {
                ["username"] = username,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok followers lookup completed.",
            () => EnsureAnyProvided("username or url", username, url));

    [Description("List accounts followed by a TikTok profile.")]
    [McpServerTool(Title = "DumplingAI get TikTok user following", Name = "dumplingai_get_tiktok_user_following", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokUserFollowing(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok username or handle.")] string? username = null,
        [Description("Optional full TikTok profile URL.")] string? url = null,
        [Description("Optional pagination cursor.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-user-following",
            new JsonObject
            {
                ["username"] = username,
                ["url"] = url,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok following lookup completed.",
            () => EnsureAnyProvided("username or url", username, url));

    [Description("Get transcript data for a TikTok video.")]
    [McpServerTool(Title = "DumplingAI get TikTok transcript", Name = "dumplingai_get_tiktok_transcript", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetTikTokTranscript(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional TikTok video ID.")] string? videoId = null,
        [Description("Optional full TikTok video URL.")] string? url = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-tiktok-transcript",
            new JsonObject
            {
                ["videoId"] = videoId,
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok transcript lookup completed.",
            () => EnsureAnyProvided("videoId or url", videoId, url));

    [Description("Search TikTok users by keyword or username.")]
    [McpServerTool(Title = "DumplingAI search TikTok users", Name = "dumplingai_search_tiktok_users", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchTikTokUsers(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The user search query.")] string query,
        [Description("Optional pagination cursor.")] string? cursor = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? limit = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search-tiktok-users",
            new JsonObject
            {
                ["query"] = query,
                ["cursor"] = cursor,
                ["limit"] = limit,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI TikTok user search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Get public LinkedIn profile data for a person.")]
    [McpServerTool(Title = "DumplingAI get LinkedIn profile", Name = "dumplingai_get_linkedin_profile", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetLinkedInProfile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The public LinkedIn profile URL.")] string url,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-linkedin-profile",
            new JsonObject
            {
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI LinkedIn profile lookup completed.",
            () => EnsureRequired(url, "url"));

    [Description("Get public LinkedIn company data.")]
    [McpServerTool(Title = "DumplingAI get LinkedIn company", Name = "dumplingai_get_linkedin_company", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetLinkedInCompany(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The public LinkedIn company URL.")] string url,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-linkedin-company",
            new JsonObject
            {
                ["url"] = url,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI LinkedIn company lookup completed.",
            () => EnsureRequired(url, "url"));

    [Description("Run a federated web search across DumplingAI search providers.")]
    [McpServerTool(Title = "DumplingAI search web", Name = "dumplingai_search_web", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchWeb(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The search query.")] string query,
        [Description("Optional page number.")][Range(1, int.MaxValue)] int? page = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? num = null,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search",
            new JsonObject
            {
                ["query"] = query,
                ["page"] = page,
                ["num"] = num,
                ["hl"] = hl,
                ["gl"] = gl,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI web search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Search news articles through DumplingAI.")]
    [McpServerTool(Title = "DumplingAI search news", Name = "dumplingai_search_news", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchNews(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The news search query.")] string query,
        [Description("Optional page number.")][Range(1, int.MaxValue)] int? page = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? num = null,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search-news",
            new JsonObject
            {
                ["query"] = query,
                ["page"] = page,
                ["num"] = num,
                ["hl"] = hl,
                ["gl"] = gl,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI news search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Search for places, businesses, and points of interest.")]
    [McpServerTool(Title = "DumplingAI search places", Name = "dumplingai_search_places", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchPlaces(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The place search query.")] string query,
        [Description("Optional page number.")][Range(1, int.MaxValue)] int? page = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? num = null,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional latitude for localized results.")] double? latitude = null,
        [Description("Optional longitude for localized results.")] double? longitude = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search-places",
            new JsonObject
            {
                ["query"] = query,
                ["page"] = page,
                ["num"] = num,
                ["hl"] = hl,
                ["gl"] = gl,
                ["latitude"] = latitude,
                ["longitude"] = longitude,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI places search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Search map results and coordinates through DumplingAI.")]
    [McpServerTool(Title = "DumplingAI search maps", Name = "dumplingai_search_maps", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_SearchMaps(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The map search query.")] string query,
        [Description("Optional page number.")][Range(1, int.MaxValue)] int? page = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? num = null,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional latitude for localized results.")] double? latitude = null,
        [Description("Optional longitude for localized results.")] double? longitude = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/search-maps",
            new JsonObject
            {
                ["query"] = query,
                ["page"] = page,
                ["num"] = num,
                ["hl"] = hl,
                ["gl"] = gl,
                ["latitude"] = latitude,
                ["longitude"] = longitude,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI maps search completed.",
            () => EnsureRequired(query, "query"));

    [Description("Get autocomplete suggestions for a partial query.")]
    [McpServerTool(Title = "DumplingAI get autocomplete", Name = "dumplingai_get_autocomplete", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetAutocomplete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The partial query text.")] string query,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-autocomplete",
            new JsonObject
            {
                ["query"] = query,
                ["hl"] = hl,
                ["gl"] = gl,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI autocomplete lookup completed.",
            () => EnsureRequired(query, "query"));

    [Description("Get recent Google reviews and ratings for a business.")]
    [McpServerTool(Title = "DumplingAI get Google reviews", Name = "dumplingai_get_google_reviews", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> DumplingAI_GetGoogleReviews(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional Google place ID.")] string? placeId = null,
        [Description("Optional Google data ID.")] string? dataId = null,
        [Description("Optional Google CID.")] string? cid = null,
        [Description("Optional Google Maps or business URL.")] string? url = null,
        [Description("Optional comment sort mode, such as newest or highest.")] string? sort = null,
        [Description("Optional page number.")][Range(1, int.MaxValue)] int? page = null,
        [Description("Optional page size or result limit.")][Range(1, 100)] int? num = null,
        [Description("Optional language code, such as en.")] string? hl = null,
        [Description("Optional country code, such as us.")] string? gl = null,
        [Description("Optional DumplingAI request source.")] string? requestSource = null,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(
            serviceProvider,
            requestContext,
            "/get-google-reviews",
            new JsonObject
            {
                ["placeId"] = placeId,
                ["dataId"] = dataId,
                ["cid"] = cid,
                ["url"] = url,
                ["sort"] = sort,
                ["page"] = page,
                ["num"] = num,
                ["hl"] = hl,
                ["gl"] = gl,
                ["requestSource"] = requestSource
            }.WithoutNulls(),
            cancellationToken,
            "DumplingAI Google reviews lookup completed.",
            () => EnsureAnyProvided("placeId, dataId, cid, or url", placeId, dataId, cid, url));

    private static void EnsureRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{name} is required.");
    }

    private static void EnsureAnyProvided(string label, params string?[] values)
    {
        if (values.All(string.IsNullOrWhiteSpace))
            throw new ValidationException($"Provide at least one of {label}.");
    }

    private static async Task<CallToolResult?> ExecuteAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        JsonObject payload,
        CancellationToken cancellationToken,
        string summary,
        Action? validate = null)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                validate?.Invoke();

                var client = serviceProvider.GetRequiredService<DumplingAIClient>();
                var response = await client.PostAsync(endpoint, payload, cancellationToken);
                var structured = DumplingAIHelpers.CreateStructuredResponse(endpoint, payload, response);

                return new CallToolResult
                {
                    Meta = await requestContext.GetToolMeta(),
                    StructuredContent = structured,
                    Content = [summary.ToTextContentBlock()]
                };
            }));
}
