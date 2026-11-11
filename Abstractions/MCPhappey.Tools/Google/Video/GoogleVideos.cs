using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Google.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Video;

public static class GoogleVideos
{
    public const string DefaultVideoModel = "gemini-omni-1.2-flash";

    [Description("Generate a Google Omni video from text, references, or first/last frames, or edit a supplied video using inline media only. Every call supplies all inputs. No input files are uploaded, no interaction is stored, and no conversation history is used. Supplied-video editing has regional restrictions enforced by Google.")]
    [McpServerTool(Title = "Google Videos: generate or edit video", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> GoogleVideos_CreateVideo(
        [Description("Prompt describing motion, camera work, scene, audio, or the requested edit.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional first-frame image URL for generation.")] string? firstFrameUrl = null,
        [Description("Optional last-frame image URL for generation; requires firstFrameUrl. Duration must be 3–10 seconds.")] string? lastFrameUrl = null,
        [Description("Optional reference image URLs for consistent subjects. At most six images total, including frames.")] string[]? referenceImageUrls = null,
        [Description("Explicit source-video URL for editing. Downloaded and sent inline, never uploaded through the Google Files API. Editing requires a clip of at most 15 seconds and is subject to regional availability.")] string? videoUrl = null,
        [Description("generate or edit. Use generate with optional first/last frames to create a new clip. edit requires videoUrl and cannot be combined with first/last frames.")] string operation = "generate",
        [Description("Omni video-generation model.")] string model = DefaultVideoModel,
        [Description("Whole seconds 3–15; first/last-frame interpolation supports 3–10 seconds.")] int durationSeconds = 10,
        [Description("16:9 or 9:16.")] string aspectRatio = "16:9",
        [Description("360p, 720p, 1080p, or 4k.")] string resolution = "720p",
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            Validate(prompt, operation, firstFrameUrl, lastFrameUrl, videoUrl, referenceImageUrls?.Length ?? 0,
                durationSeconds, aspectRatio, resolution);
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            var input = new JsonArray();
            if (!string.IsNullOrWhiteSpace(videoUrl))
            {
                var video = await serviceProvider.GetMediaInputAsync(requestContext.Server, videoUrl, "video", cancellationToken);
                input.Add(GoogleInteractionInput.Bytes("video", video.Contents, video.MimeType));
            }
            foreach (var url in new[] { firstFrameUrl, lastFrameUrl }.Where(Has).Concat(referenceImageUrls ?? []))
            {
                var image = await serviceProvider.GetMediaInputAsync(requestContext.Server, url!, "image", cancellationToken);
                input.Add(GoogleInteractionInput.Bytes("image", image.Contents, image.MimeType));
            }
            var instructions = operation switch
            {
                "edit" => "Edit the supplied video according to these instructions. Keep everything else the same. ",
                _ when Has(lastFrameUrl) => "Use the first image as the starting frame and the second image as the ending frame. Other images are subject references. ",
                _ when Has(firstFrameUrl) => "Use the first image as the starting frame. Other images are subject references. ",
                _ => ""
            };
            input.Add(GoogleInteractionInput.Text(instructions + prompt));
            var request = BuildRequest(model, input, durationSeconds, aspectRatio, resolution);
            var interaction = await serviceProvider.GetRequiredService<GoogleInteractionsClient>()
                .CreateInteractionAsync(request, cancellationToken, requestContext.Server);
            return await interaction.ToToolResultAsync(requestContext, serviceProvider, cancellationToken, requiredMediaType: "video");
        });

    [Description("Answer a question about an explicitly supplied video with a Google video-understanding model. The video is downloaded and sent inline; no Google file upload or stored conversation is used.")]
    [McpServerTool(Title = "Google Videos: ask about video", ReadOnly = true, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> GoogleVideos_AskVideo(
        [Description("Question about the video, such as shot composition, action, timing, continuity, or dialogue.")] string question,
        [Description("Public HTTPS video URL or SharePoint/OneDrive URL with delegated access.")] string videoUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video-understanding model, not an Omni video-generation model.")] string model = GoogleInteractionsClient.DefaultModel,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(question);
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
            var video = await serviceProvider.GetMediaInputAsync(requestContext.Server, videoUrl, "video", cancellationToken);
            var input = new JsonArray(GoogleInteractionInput.Bytes("video", video.Contents, video.MimeType), GoogleInteractionInput.Text(question));
            var text = await serviceProvider.GetRequiredService<GoogleInteractionsClient>().CreateTextInteractionAsync(
                new GoogleInteractionRequest { Model = model, Input = input }, cancellationToken, requestContext.Server);
            return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
        });

    internal static GoogleInteractionRequest BuildRequest(string model, JsonArray input, int duration, string aspectRatio, string resolution)
        => new()
        {
            Model = model, Input = input, Store = false, Stream = false, Background = false,
            ResponseFormat = new JsonObject
            {
                ["type"] = "video", ["delivery"] = "inline", ["duration"] = duration + "s",
                ["aspect_ratio"] = aspectRatio, ["resolution"] = resolution
            }
            // No system instruction, forced task, reasoning settings, or previous interaction.
        };

    internal static void Validate(string prompt, string operation, string? first, string? last, string? video,
        int references, int duration, string aspectRatio, string resolution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (operation is not ("generate" or "edit")) throw new ArgumentException("Use generate or edit.");
        if (Has(last) && !Has(first)) throw new ArgumentException("A last frame requires a first frame.");
        if (operation == "generate" && Has(video)) throw new ArgumentException("Select edit when supplying a source video.");
        if (operation != "generate" && (!Has(video) || Has(first) || Has(last)))
            throw new ArgumentException("Editing requires a source video and cannot be combined with first/last frames.");
        if (references < 0 || references + (Has(first) ? 1 : 0) + (Has(last) ? 1 : 0) > 6)
            throw new ArgumentException("Google Omni supports at most six supplied images, including frames.");
        if (duration < 3 || duration > (Has(last) ? 10 : 15))
            throw new ArgumentException("Google Omni duration must be 3–15 seconds, or 3–10 for first/last-frame interpolation.");
        if (aspectRatio is not ("16:9" or "9:16")) throw new ArgumentException("Use 16:9 or 9:16 aspect ratio.");
        if (resolution is not ("360p" or "720p" or "1080p" or "4k")) throw new ArgumentException("Use 360p, 720p, 1080p, or 4k resolution.");
    }

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
}
