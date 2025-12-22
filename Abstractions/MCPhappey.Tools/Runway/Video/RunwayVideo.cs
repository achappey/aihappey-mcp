using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using System.Runtime.Serialization;

namespace MCPhappey.Tools.Runway.Video;

public static class RunwayVideo
{

    // ---------- TEXT → VIDEO ----------
    [Description("Start a Runway text-to-video task. Returns the task id.")]
    [McpServerTool(Title = "Create Runway text-to-video", Name = "runway_text_to_video",
        OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_TextToVideo(
        string? promptText,
        RunwayTextToVideoModel? model,
        string? ratio,
        int? duration,
        int? seed,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        CancellationToken ct = default) =>
        await rc.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewVideoRequest
        {
            PromptText = promptText ?? "",
            Model = model ?? RunwayTextToVideoModel.Veo31Fast,
            Ratio = string.IsNullOrWhiteSpace(ratio) ? "1280:720" : ratio!,
            Duration = duration,
            Seed = seed
        }, ct);

        if (!typed.Duration.HasValue)
            typed.Duration = typed.Model == RunwayTextToVideoModel.Veo3 ? 8 : 6;

        Validate(typed);

        var runway = sp.GetRequiredService<RunwayClient>();

        var payload = new
        {
            promptText = typed.PromptText,
            model = typed.Model.GetEnumMemberValue(),
            ratio = typed.Ratio,
            duration = typed.Duration,
            seed = typed.Seed
        };

        var json = await runway.TextToVideoAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "mp4", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });


    // ---------- IMAGE → VIDEO ----------
    [Description("Start an image-to-video task on Runway. Returns the task id.")]
    [McpServerTool(Title = "Runway image-to-video", Name = "runway_image_to_video_create",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Runway_ImageToVideoCreate(
        IEnumerable<string> promptImages,
        string? promptText,
        RunwayImageToVideoModel? model,
        [Description("Ratio.")] RunwayImageToVideoRatio? ratio,
        int? duration,
        int? seed,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
    {
        if (promptImages == null || !promptImages.Any())
            throw new ValidationException("At least one prompt image is required.");

        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewImageToVideo
        {
            PromptText = promptText,
            Model = model ?? RunwayImageToVideoModel.Gen4Turbo,
            Ratio = ratio ?? RunwayImageToVideoRatio.Ratio1280x720,
            Duration = duration ?? 6,
            Seed = seed
        }, ct);

        if (typed.Duration < 2 || typed.Duration > 10)
            throw new ValidationException("Duration must be between 2 and 10 seconds depending on model.");

        var downloadService = sp.GetRequiredService<DownloadService>();
        var runway = sp.GetRequiredService<RunwayClient>();

        // Convert each image to a base64 data URI
        var imagePayload = new List<object>();
        string? position = null;

        foreach (var img in promptImages)
        {
            var files = await downloadService.DownloadContentAsync(sp, rc.Server, img, ct);
            var bytes = files.FirstOrDefault() ?? throw new Exception($"Failed to download image: {img}");

            imagePayload.Add(new { uri = bytes.ToDataUri(), position = position == null ? "first" : "last" });
            position = "first";
        }

        var payload = new
        {
            promptImage = imagePayload, // always an array
            seed = typed.Seed,
            model = typed.Model.GetEnumMemberValue(),
            promptText = typed.PromptText,
            duration = typed.Duration,
            ratio = typed.Ratio.GetEnumMemberValue()
        };

        var json = await runway.ImageToVideoAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "mp4", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });

    private static void Validate(RunwayNewVideoRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.PromptText))
            throw new ValidationException("promptText is required.");
        if (input.PromptText.Length > 1000)
            throw new ValidationException("promptText must be at most 1000 characters.");

        if (!input.Duration.HasValue)
            throw new ValidationException("duration is required.");

        if (input.Seed is < 0 or > int.MaxValue)
            throw new ValidationException("seed must be between 0 and 4294967295.");
    }

    // ---------- CHARACTER PERFORMANCE ----------
    [Description("Control a character’s expressions and movements using a reference video.")]
    [McpServerTool(Title = "Create Runway Character Performance", Name = "runway_character_performance", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_CharacterPerformance(
        string characterType,
        string characterUri,
        string referenceUri,
        string? ratio,
        [Description("Intensity of expressions."), Range(1, 5)] int? expressionIntensity,
        [Description("Enable body control for gestures and movements.")] bool? bodyControl,
        string? publicFigureThreshold,
        int? seed,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewCharacterPerformance
        {
            CharacterType = string.IsNullOrWhiteSpace(characterType) ? "image" : characterType!,
            Ratio = string.IsNullOrWhiteSpace(ratio) ? "1280:720" : ratio!,
            ExpressionIntensity = expressionIntensity ?? 3,
            BodyControl = bodyControl ?? true,
            PublicFigureThreshold = string.IsNullOrWhiteSpace(publicFigureThreshold) ? "auto" : publicFigureThreshold!,
            Seed = seed
        }, ct);

        ValidateCharacterPerformance(typed);

        var downloadService = sp.GetRequiredService<DownloadService>();

        // Download and encode character file
        var charFiles = await downloadService.DownloadContentAsync(sp, rc.Server, characterUri, ct);
        var charBytes = charFiles.FirstOrDefault()?.Contents ?? throw new Exception($"Failed to download character: {characterUri}");
        string charBase64 = Convert.ToBase64String(charBytes.ToArray());
        string charMime = characterType == "video" ? "video/mp4" : "image/png";
        string charDataUri = $"data:{charMime};base64,{charBase64}";

        // Download and encode reference video
        var refFiles = await downloadService.DownloadContentAsync(sp, rc.Server, referenceUri, ct);
        var refBytes = refFiles.FirstOrDefault() ?? throw new Exception($"Failed to download reference video: {referenceUri}");
        string refDataUri = refBytes.ToDataUri();

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = "act_two",
            seed = typed.Seed,
            character = new
            {
                type = typed.CharacterType,
                uri = charDataUri
            },
            reference = new
            {
                type = "video",
                uri = refDataUri
            },
            bodyControl = typed.BodyControl,
            expressionIntensity = typed.ExpressionIntensity,
            ratio = typed.Ratio,
            contentModeration = new
            {
                publicFigureThreshold = typed.PublicFigureThreshold
            }
        };

        return await runway.CharacterPerformanceAsync(payload, ct);
    }));

    private static void ValidateCharacterPerformance(RunwayNewCharacterPerformance input)
    {
        if (string.IsNullOrWhiteSpace(input.CharacterType) || (input.CharacterType != "image" && input.CharacterType != "video"))
            throw new ValidationException("CharacterType must be 'image' or 'video'.");
        if (input.ExpressionIntensity is < 1 or > 5)
            throw new ValidationException("ExpressionIntensity must be between 1 and 5.");
        if (input.PublicFigureThreshold != "auto" && input.PublicFigureThreshold != "low")
            throw new ValidationException("PublicFigureThreshold must be 'auto' or 'low'.");
    }


    // ---------- VIDEO UPSCALE ----------
    [Description("Upscale a video by 4× using Runway’s upscaling model (max 4096px).")]
    [McpServerTool(Title = "Create Runway Video Upscale", Name = "runway_video_upscale", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_VideoUpscale(
        [Description("The video to upscale. SharePoint and OneDrive links are supported.")] string videoUri,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the upscale is fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        CancellationToken ct = default) =>
        await rc.WithExceptionCheck(async () =>
    {
        var downloadService = sp.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(sp, rc.Server, videoUri, ct);
        var bytes = files.FirstOrDefault() ?? throw new Exception($"Failed to download video: {videoUri}");
        string dataUri = bytes.ToDataUri();

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = "upscale_v1",
            videoUri = dataUri
        };

        // 4️⃣ Start the task
        var json = await runway.VideoUpscaleAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "mp4", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });

    [Description("Typed input for Runway video upscaling.")]
    public class RunwayNewVideoUpscale
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("Model name, must be 'upscale_v1'.")]
        public string Model { get; set; } = default!;
    }


    // ---------- VIDEO TO VIDEO ----------
    [Description("Generate a new video from a source video and text prompt using Runway’s Gen4 model.")]
    [McpServerTool(Title = "Create Runway Video-to-Video", Name = "runway_video_to_video", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_VideoToVideo(
        string videoUri,
        string promptText,
        string? ratio,
        int? seed,
        string? referenceImageUri,
        string? publicFigureThreshold,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        CancellationToken ct = default) =>
         await rc.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewVideoToVideo
        {
            PromptText = promptText,
            Ratio = string.IsNullOrWhiteSpace(ratio) ? "1280:720" : ratio!,
            Seed = seed,
            PublicFigureThreshold = string.IsNullOrWhiteSpace(publicFigureThreshold) ? "auto" : publicFigureThreshold!,
        }, ct);

        ValidateVideoToVideo(typed);

        var downloadService = sp.GetRequiredService<DownloadService>();

        // Encode source video
        var vidFiles = await downloadService.DownloadContentAsync(sp, rc.Server, videoUri, ct);
        var vidBytes = vidFiles.FirstOrDefault() ?? throw new Exception($"Failed to download video: {videoUri}");
        string vidDataUri = vidBytes.ToDataUri();

        // Encode reference image if provided
        object[]? references = null;
        if (!string.IsNullOrWhiteSpace(referenceImageUri))
        {
            var imgFiles = await downloadService.DownloadContentAsync(sp, rc.Server, referenceImageUri, ct);
            var imgBytes = imgFiles.FirstOrDefault() ?? throw new Exception($"Failed to download reference image: {referenceImageUri}");
            references = [new { type = "image", uri = imgBytes.ToDataUri() }];
        }

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            model = "gen4_aleph",
            videoUri = vidDataUri,
            promptText = typed.PromptText,
            ratio = typed.Ratio,
            seed = typed.Seed,
            references,
            contentModeration = new
            {
                publicFigureThreshold = typed.PublicFigureThreshold
            }
        };

        var json = await runway.VideoToVideoAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "mp4", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });

    private static void ValidateVideoToVideo(RunwayNewVideoToVideo input)
    {
        if (string.IsNullOrWhiteSpace(input.PromptText))
            throw new ValidationException("PromptText is required.");
        if (input.PromptText.Length > 1000)
            throw new ValidationException("PromptText must be at most 1000 characters.");
        if (string.IsNullOrWhiteSpace(input.Ratio))
            throw new ValidationException("Ratio is required.");
        if (input.PublicFigureThreshold != "auto" && input.PublicFigureThreshold != "low")
            throw new ValidationException("PublicFigureThreshold must be 'auto' or 'low'.");
    }


}


[Description("Typed input for Runway video-to-video generation.")]
public class RunwayNewVideoToVideo
{
    [JsonPropertyName("promptText")]
    [Required]
    [Description("Prompt describing the desired transformation.")]
    public string PromptText { get; set; } = default!;

    [JsonPropertyName("ratio")]
    [Required]
    [Description("Output ratio, e.g., 1280:720, 720:1280, etc.")]
    public string Ratio { get; set; } = default!;

    [JsonPropertyName("seed")]
    [Description("Optional seed for reproducibility.")]
    public int? Seed { get; set; }

    [JsonPropertyName("publicFigureThreshold")]
    [Description("Moderation strictness, 'auto' or 'low'.")]
    public string PublicFigureThreshold { get; set; } = default!;
}
// -------- DTOs for elicitation --------

[Description("Typed input for Runway text-to-video creation.")]
public class RunwayNewVideoRequest
{
    [JsonPropertyName("promptText")]
    [Required]
    [Description("Prompt describing the video to generate. Non-empty; up to 1000 UTF-16 code units.")]
    public string PromptText { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model variant.")]
    public RunwayTextToVideoModel Model { get; set; } = default!;

    [JsonPropertyName("ratio")]
    [Required]
    [Description("Aspect ratio string. One of: 1280:720 | 720:1280 | 1080:1920 | 1920:1080.")]
    public string Ratio { get; set; } = default!;

    [JsonPropertyName("duration")]
    [Description("Video duration in seconds (4, 6, or 8). For 'veo3' the duration must be 8.")]
    public int? Duration { get; set; }

    [JsonPropertyName("seed")]
    [Description("Optional seed for reproducibility. Integer in the inclusive range [0, 4294967295].")]
    [Range(0, int.MaxValue)]
    public int? Seed { get; set; }
}

[Description("Typed input for Runway image-to-video creation.")]
public class RunwayNewImageToVideo
{
    [JsonPropertyName("promptText")]
    [Description("Optional text prompt to describe the desired content.")]
    public string? PromptText { get; set; }

    [JsonPropertyName("model")]
    [Required]
    [Description("Model variant.")]
    public RunwayImageToVideoModel Model { get; set; } = default!;

    [JsonPropertyName("ratio")]
    [Required]
    [Description("Output ratio.")]
    public RunwayImageToVideoRatio Ratio { get; set; } = RunwayImageToVideoRatio.Ratio1280x720;

    [JsonPropertyName("duration")]
    [Required]
    [Description("Video duration in seconds. Typically 2–10 depending on model. For example, veo3 requires 8; veo3.1/veo3.1_fast allow 4, 6, or 8.")]
    public int Duration { get; set; }

    [JsonPropertyName("seed")]
    [Description("Optional seed in [0..4294967295] for reproducibility.")]
    [Range(0, int.MaxValue)]
    public int? Seed { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunwayImageToVideoModel
{
    [EnumMember(Value = "gen4_turbo")]
    Gen4Turbo,

    [EnumMember(Value = "gen3a_turbo")]
    Gen3aTurbo,

    [EnumMember(Value = "veo3.1")]
    Veo31,

    [EnumMember(Value = "veo3.1_fast")]
    Veo31Fast,

    [EnumMember(Value = "veo3")]
    Veo3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunwayImageToVideoRatio
{
    [EnumMember(Value = "1280:720")]
    Ratio1280x720,

    [EnumMember(Value = "720:1280")]
    Ratio720x1280,

    [EnumMember(Value = "1920:1080")]
    Ratio1920x1080,

    [EnumMember(Value = "1080:1920")]
    Ratio1080x1920,

    [EnumMember(Value = "1104:832")]
    Ratio1104x832,

    [EnumMember(Value = "832:1104")]
    Ratio832x1104,

    [EnumMember(Value = "960:960")]
    Ratio960x960,

    [EnumMember(Value = "1584:672")]
    Ratio1584x672
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunwayTextToVideoModel
{
    [EnumMember(Value = "veo3.1")]
    Veo31,

    [EnumMember(Value = "veo3.1_fast")]
    Veo31Fast,

    [EnumMember(Value = "veo3")]
    Veo3
}

[Description("Typed input for Runway character performance.")]
public class RunwayNewCharacterPerformance
{
    [JsonPropertyName("characterType")]
    [Required]
    [Description("Type of character input ('image' or 'video').")]
    public string CharacterType { get; set; } = default!;

    [JsonPropertyName("ratio")]
    [Description("Output ratio, e.g., 1280:720, 720:1280, 960:960.")]
    public string Ratio { get; set; } = default!;

    [JsonPropertyName("expressionIntensity")]
    [Description("Intensity of expressions (1–5).")]
    public int ExpressionIntensity { get; set; }

    [JsonPropertyName("bodyControl")]
    [Description("Enable body control for gestures and movements.")]
    public bool BodyControl { get; set; }

    [JsonPropertyName("publicFigureThreshold")]
    [Description("Moderation strictness, 'auto' or 'low'.")]
    public string PublicFigureThreshold { get; set; } = default!;

    [JsonPropertyName("seed")]
    [Description("Optional integer seed for reproducibility.")]
    public int? Seed { get; set; }
}
