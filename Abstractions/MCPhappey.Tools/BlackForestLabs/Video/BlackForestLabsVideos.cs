using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.BlackForestLabs.Video;

public static class BlackForestLabsVideos
{
    [Description("Generate video with native FLUX 3, optionally from first/last frames, or continue an explicitly supplied video. No MCP session or conversation state is retained. Polling completes within this call.")]
    [McpServerTool(Title = "Black Forest Labs Videos: generate or continue video", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> BlackForestLabsVideos_CreateVideo(
        [Description("Video prompt describing motion, camera work, scene, and desired audio.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional first-frame image URL.")] string? firstFrameUrl = null,
        [Description("Optional last-frame image URL; requires firstFrameUrl.")] string? lastFrameUrl = null,
        [Description("Optional source video URL to continue. Cannot be combined with frame URLs.")] string? sourceVideoUrl = null,
        [Description("Whole seconds 5–20; continuation supports 5–15. Omit for automatic duration.")] int? durationSeconds = null,
        [Description("auto, 21:9, 2:1, 16:9, 4:3, 1:1, 3:4, 9:16, or 9:21.")] string aspectRatio = "auto",
        [Description("hd, fhd, qhd, or uhd.")] string resolution = "hd",
        [Description("Generate synchronized audio.")] bool generateAudio = true,
        [Description("Generate a fast preview and return an explicit draft-cache artifact for later enhancement.")] bool draft = false,
        [Description("Moderation tolerance 0–7; organization approval applies.")] int safetyTolerance = 2,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var payload = BuildPayload(prompt, firstFrameUrl, lastFrameUrl, sourceVideoUrl,
                durationSeconds, aspectRatio, resolution, generateAudio, draft, safetyTolerance);
            if (Has(firstFrameUrl))
            {
                var frames = new JsonArray();
                foreach (var url in new[] { firstFrameUrl, lastFrameUrl }.Where(Has))
                {
                    var frame = await serviceProvider.GetMediaInputAsync(requestContext.Server, url!, "image", cancellationToken);
                    frames.Add(Convert.ToBase64String(frame.Contents.ToArray()));
                }
                payload["keyframes"] = frames;
            }
            if (Has(sourceVideoUrl))
            {
                var video = await serviceProvider.GetMediaInputAsync(requestContext.Server, sourceVideoUrl!, "video", cancellationToken);
                payload["start_video"] = Convert.ToBase64String(video.Contents.ToArray());
            }
            return await GenerateAndDeliverAsync(payload, draft, filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Render full-quality FLUX 3 video from an explicitly supplied draft-cache bundle URL. The artifact contains the original generation inputs; no conversation or earlier MCP call is referenced.")]
    [McpServerTool(Title = "Black Forest Labs Videos: enhance draft", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> BlackForestLabsVideos_EnhanceDraft(
        [Description("Draft-cache binary artifact URL from a previous draft result; not the preview-video URL.")] string draftCacheUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("hd, fhd, qhd, or uhd.")] string resolution = "fhd",
        [Description("Moderation tolerance 0–7; organization approval applies.")] int safetyTolerance = 2,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ValidateOutputOptions(resolution, safetyTolerance);
            var cache = await serviceProvider.GetMediaInputAsync(requestContext.Server, draftCacheUrl, "artifact", cancellationToken);
            var payload = new JsonObject { ["mode"] = "draft_enhance", ["draft_cache"] = Convert.ToBase64String(cache.Contents.ToArray()),
                ["resolution"] = resolution, ["safety_tolerance"] = safetyTolerance };
            return await GenerateAndDeliverAsync(payload, false, filename, serviceProvider, requestContext, cancellationToken);
        });

    internal static JsonObject BuildPayload(string prompt, string? first, string? last, string? source,
        int? duration, string aspectRatio, string resolution, bool audio, bool draft, int safety)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (Has(last) && !Has(first)) throw new ArgumentException("A last frame requires a first frame.");
        if (Has(source) && (Has(first) || Has(last))) throw new ArgumentException("Video continuation cannot be combined with first/last frames.");
        if (duration.HasValue && (duration < 5 || duration > (Has(source) ? 15 : 20)))
            throw new ArgumentException("FLUX 3 duration must be 5–20 whole seconds, or 5–15 for video continuation.");
        if (aspectRatio is not ("auto" or "21:9" or "2:1" or "16:9" or "4:3" or "1:1" or "3:4" or "9:16" or "9:21"))
            throw new ArgumentException("Unsupported FLUX 3 video aspect ratio.");
        ValidateOutputOptions(resolution, safety);
        return new JsonObject
        {
            ["mode"] = Has(source) ? "v2v" : Has(first) ? "i2v" : "t2v", ["prompt"] = prompt,
            ["duration"] = duration.HasValue ? JsonValue.Create(duration.Value) : JsonValue.Create("auto"),
            ["aspect_ratio"] = aspectRatio, ["resolution"] = resolution, ["generate_audio"] = audio,
            ["draft"] = draft, ["safety_tolerance"] = safety, ["version"] = "latest"
        };
    }

    private static async Task<CallToolResult> GenerateAndDeliverAsync(JsonObject payload, bool draft, string? filename,
        IServiceProvider services, RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        var client = services.GetRequiredService<BlackForestLabsClient>();
        var result = await client.GenerateAsync(context.Server, "flux-3-video", payload, token);
        var sample = result["sample"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(sample)) throw new InvalidOperationException("BFL returned no generated video sample.");
        var name = Path.GetFileNameWithoutExtension(filename ?? context.ToOutputFileName("mp4"));
        var outputs = new List<MediaOutput> { await client.DownloadOutputAsync(sample, name + ".mp4", "video/mp4", token) };
        if (draft)
        {
            var cache = result["draft_cache"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(cache)) throw new InvalidOperationException("BFL draft generation returned no draft-cache artifact.");
            outputs.Add(await client.DownloadOutputAsync(cache, name + "-draft-cache.bin", "application/octet-stream", token));
        }
        return await context.Server.ToMediaCallToolResult(services, outputs, token);
    }

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
    private static void ValidateOutputOptions(string resolution, int safety)
    {
        if (resolution is not ("hd" or "fhd" or "qhd" or "uhd")) throw new ArgumentException("Use hd, fhd, qhd, or uhd resolution.");
        if (safety is < 0 or > 7) throw new ArgumentException("Safety tolerance must be from 0 to 7.");
    }
}
