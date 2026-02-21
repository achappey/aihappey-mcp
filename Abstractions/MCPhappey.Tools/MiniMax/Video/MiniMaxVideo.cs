using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.MiniMax.Video;

public static class MiniMaxVideo
{
    private const string VIDEO_GENERATION_URL = "/v1/video_generation";
    private const string VIDEO_QUERY_URL = "/v1/query/video_generation";
    private const string FILE_RETRIEVE_URL = "/v1/files/retrieve";

    private static async Task<CallToolResult> CheckAndUploadAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string taskId,
        string? filename = null,
        bool waitUntilCompleted = false,
        TimeSpan? checkInterval = null,
        CancellationToken cancellationToken = default)
    {
        var minimax = serviceProvider.GetRequiredService<MiniMaxClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        checkInterval ??= TimeSpan.FromSeconds(10);

        async Task<JsonDocument> QueryTaskAsync()
            => await minimax.GetAsync(VIDEO_QUERY_URL, new Dictionary<string, string?>
            {
                ["task_id"] = taskId
            }, cancellationToken);

        JsonDocument taskDoc;
        string status;

        do
        {
            taskDoc = await QueryTaskAsync();
            status = taskDoc.RootElement.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "Unknown"
                : "Unknown";

            if (!waitUntilCompleted || status.Equals("Success", StringComparison.OrdinalIgnoreCase) || status.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                break;

            await Task.Delay(checkInterval.Value, cancellationToken);
        }
        while (!cancellationToken.IsCancellationRequested);

        if (!status.Equals("Success", StringComparison.OrdinalIgnoreCase))
        {
            return new CallToolResult
            {
                Content =
                [
                    taskDoc.ToJsonContent(VIDEO_QUERY_URL),
                    $"⌛ Task `{taskId}` is **{status}**. Please check again later.".ToTextContentBlock()
                ]
            };
        }

        var fileId = taskDoc.RootElement.TryGetProperty("file_id", out var fileIdProp)
            ? fileIdProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(fileId))
            throw new Exception("MiniMax task completed, but no file_id was returned.");

        var retrieveDoc = await minimax.GetAsync(FILE_RETRIEVE_URL, new Dictionary<string, string?>
        {
            ["file_id"] = fileId
        }, cancellationToken);

        var downloadUrl = retrieveDoc.RootElement.TryGetProperty("file", out var fileObj)
            && fileObj.ValueKind == JsonValueKind.Object
            && fileObj.TryGetProperty("download_url", out var urlProp)
            ? urlProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new Exception("MiniMax file retrieve succeeded, but no download_url was returned.");

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, downloadUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("Unable to download generated MiniMax video.");

        var outputName = $"{(string.IsNullOrWhiteSpace(filename) ? taskId : filename)}.mp4";
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            outputName,
            BinaryData.FromBytes(file.Contents.ToArray()),
            cancellationToken);

        return new CallToolResult
        {
            Content =
            [
                taskDoc.ToJsonContent(VIDEO_QUERY_URL),
                retrieveDoc.ToJsonContent(FILE_RETRIEVE_URL),
                uploaded!
            ]
        };
    }

    [Description("Create a MiniMax text-to-video generation task.")]
    [McpServerTool(Title = "Create text-to-video task with MiniMax", Name = "minimax_video_text_to_video_create", Destructive = false)]
    public static async Task<CallToolResult?> MiniMaxVideo_TextToVideoCreate(
        [Description("Prompt describing the video content."), MaxLength(2000)] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video model.")] MiniMaxTextToVideoModel model = MiniMaxTextToVideoModel.MiniMaxHailuo23,
        [Description("Prompt optimizer.")] bool promptOptimizer = true,
        [Description("Fast pretreatment when optimizer is enabled.")] bool fastPretreatment = false,
        [Description("Duration in seconds.")] int? duration = null,
        [Description("Resolution.")] MiniMaxVideoResolution? resolution = null,
        [Description("Output filename without extension. Defaults to autogenerated name.")] string? filename = null,
        [Description("If true, poll until completed and upload generated video.")] bool waitUntilCompleted = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new MiniMaxTextToVideoRequest
            {
                Prompt = prompt,
                Model = model,
                PromptOptimizer = promptOptimizer,
                FastPretreatment = fastPretreatment,
                Duration = duration,
                Resolution = resolution
            },
            cancellationToken);

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "User input missing.".ToErrorCallToolResponse();

        var minimax = serviceProvider.GetRequiredService<MiniMaxClient>();
        var body = new
        {
            model = typed.Model.GetEnumMemberValue(),
            prompt = typed.Prompt,
            prompt_optimizer = typed.PromptOptimizer,
            fast_pretreatment = typed.FastPretreatment,
            duration = typed.Duration,
            resolution = typed.Resolution?.GetEnumMemberValue()
        };

        var doc = await minimax.PostAsync(VIDEO_GENERATION_URL, body, cancellationToken);
        if (waitUntilCompleted)
        {
            var taskId = doc.RootElement.TryGetProperty("task_id", out var taskIdProp)
                ? taskIdProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(taskId))
                throw new Exception("No task_id returned by MiniMax video generation API.");

            return await CheckAndUploadAsync(
                serviceProvider,
                requestContext,
                taskId,
                filename,
                waitUntilCompleted: true,
                cancellationToken: cancellationToken);
        }

        return doc.ToJsonContent(VIDEO_GENERATION_URL).ToCallToolResult();
    });

    [Description("Create a MiniMax image-to-video generation task.")]
    [McpServerTool(Title = "Create image-to-video task with MiniMax", Name = "minimax_video_image_to_video_create", Destructive = false)]
    public static async Task<CallToolResult?> MiniMaxVideo_ImageToVideoCreate(
        [Description("First frame image URL or Data URL.")] string firstFrameImage,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Video model.")] MiniMaxImageToVideoModel model = MiniMaxImageToVideoModel.MiniMaxHailuo23,
        [Description("Prompt describing the target motion/content."), MaxLength(2000)] string? prompt = null,
        [Description("Prompt optimizer.")] bool promptOptimizer = true,
        [Description("Fast pretreatment when optimizer is enabled.")] bool fastPretreatment = false,
        [Description("Duration in seconds.")] int? duration = null,
        [Description("Resolution.")] MiniMaxImageToVideoResolution? resolution = null,
        [Description("Output filename without extension. Defaults to autogenerated name.")] string? filename = null,
        [Description("If true, poll until completed and upload generated video.")] bool waitUntilCompleted = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new MiniMaxImageToVideoRequest
            {
                Model = model,
                FirstFrameImage = firstFrameImage,
                Prompt = prompt,
                PromptOptimizer = promptOptimizer,
                FastPretreatment = fastPretreatment,
                Duration = duration,
                Resolution = resolution
            },
            cancellationToken);

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "User input missing.".ToErrorCallToolResponse();

        var minimax = serviceProvider.GetRequiredService<MiniMaxClient>();
        var body = new
        {
            model = typed.Model.GetEnumMemberValue(),
            first_frame_image = typed.FirstFrameImage,
            prompt = typed.Prompt,
            prompt_optimizer = typed.PromptOptimizer,
            fast_pretreatment = typed.FastPretreatment,
            duration = typed.Duration,
            resolution = typed.Resolution?.GetEnumMemberValue()
        };

        var doc = await minimax.PostAsync(VIDEO_GENERATION_URL, body, cancellationToken);
        if (waitUntilCompleted)
        {
            var taskId = doc.RootElement.TryGetProperty("task_id", out var taskIdProp)
                ? taskIdProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(taskId))
                throw new Exception("No task_id returned by MiniMax video generation API.");

            return await CheckAndUploadAsync(
                serviceProvider,
                requestContext,
                taskId,
                filename,
                waitUntilCompleted: true,
                cancellationToken: cancellationToken);
        }

        return doc.ToJsonContent(VIDEO_GENERATION_URL).ToCallToolResult();
    });

    [Description("Create a MiniMax start-and-end-frame video generation task.")]
    [McpServerTool(Title = "Create start/end-frame video task with MiniMax", Name = "minimax_video_start_end_to_video_create", Destructive = false)]
    public static async Task<CallToolResult?> MiniMaxVideo_StartEndToVideoCreate(
        [Description("Last frame image URL or Data URL.")] string lastFrameImage,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional first frame image URL or Data URL.")] string? firstFrameImage = null,
        [Description("Optional prompt describing transitions and motion."), MaxLength(2000)] string? prompt = null,
        [Description("Prompt optimizer.")] bool promptOptimizer = true,
        [Description("Duration in seconds.")] int? duration = null,
        [Description("Resolution.")] MiniMaxStartEndResolution? resolution = null,
        [Description("Output filename without extension. Defaults to autogenerated name.")] string? filename = null,
        [Description("If true, poll until completed and upload generated video.")] bool waitUntilCompleted = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new MiniMaxStartEndToVideoRequest
            {
                FirstFrameImage = firstFrameImage,
                LastFrameImage = lastFrameImage,
                Prompt = prompt,
                PromptOptimizer = promptOptimizer,
                Duration = duration,
                Resolution = resolution
            },
            cancellationToken);

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "User input missing.".ToErrorCallToolResponse();

        var minimax = serviceProvider.GetRequiredService<MiniMaxClient>();
        var body = new
        {
            model = "MiniMax-Hailuo-02",
            prompt = typed.Prompt,
            first_frame_image = typed.FirstFrameImage,
            last_frame_image = typed.LastFrameImage,
            prompt_optimizer = typed.PromptOptimizer,
            duration = typed.Duration,
            resolution = typed.Resolution?.GetEnumMemberValue()

        };

        var doc = await minimax.PostAsync(VIDEO_GENERATION_URL, body, cancellationToken);
        if (waitUntilCompleted)
        {
            var taskId = doc.RootElement.TryGetProperty("task_id", out var taskIdProp)
                ? taskIdProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(taskId))
                throw new Exception("No task_id returned by MiniMax video generation API.");

            return await CheckAndUploadAsync(
                serviceProvider,
                requestContext,
                taskId,
                filename,
                waitUntilCompleted: true,
                cancellationToken: cancellationToken);
        }

        return doc.ToJsonContent(VIDEO_GENERATION_URL).ToCallToolResult();
    });
}

[Description("Please confirm the MiniMax text-to-video request.")]
public class MiniMaxTextToVideoRequest
{
    [JsonPropertyName("model")]
    [Required]
    public MiniMaxTextToVideoModel Model { get; set; } = MiniMaxTextToVideoModel.MiniMaxHailuo23;

    [JsonPropertyName("prompt")]
    [Required]
    [MaxLength(2000)]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("prompt_optimizer")]
    public bool PromptOptimizer { get; set; } = true;

    [JsonPropertyName("fast_pretreatment")]
    public bool FastPretreatment { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("resolution")]
    public MiniMaxVideoResolution? Resolution { get; set; }

}

[Description("Please confirm the MiniMax image-to-video request.")]
public class MiniMaxImageToVideoRequest
{
    [JsonPropertyName("model")]
    [Required]
    public MiniMaxImageToVideoModel Model { get; set; } = MiniMaxImageToVideoModel.MiniMaxHailuo23;

    [JsonPropertyName("first_frame_image")]
    [Required]
    public string FirstFrameImage { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [MaxLength(2000)]
    public string? Prompt { get; set; }

    [JsonPropertyName("prompt_optimizer")]
    public bool PromptOptimizer { get; set; } = true;

    [JsonPropertyName("fast_pretreatment")]
    public bool FastPretreatment { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("resolution")]
    public MiniMaxImageToVideoResolution? Resolution { get; set; }

}

[Description("Please confirm the MiniMax start/end-frame video request.")]
public class MiniMaxStartEndToVideoRequest
{
    [JsonPropertyName("first_frame_image")]
    public string? FirstFrameImage { get; set; }

    [JsonPropertyName("last_frame_image")]
    [Required]
    public string LastFrameImage { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [MaxLength(2000)]
    public string? Prompt { get; set; }

    [JsonPropertyName("prompt_optimizer")]
    public bool PromptOptimizer { get; set; } = true;

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("resolution")]
    public MiniMaxStartEndResolution? Resolution { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxTextToVideoModel
{
    [EnumMember(Value = "MiniMax-Hailuo-2.3")] MiniMaxHailuo23,
    [EnumMember(Value = "MiniMax-Hailuo-02")] MiniMaxHailuo02,
    [EnumMember(Value = "T2V-01-Director")] T2V01Director,
    [EnumMember(Value = "T2V-01")] T2V01
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxImageToVideoModel
{
    [EnumMember(Value = "MiniMax-Hailuo-2.3")] MiniMaxHailuo23,
    [EnumMember(Value = "MiniMax-Hailuo-2.3-Fast")] MiniMaxHailuo23Fast,
    [EnumMember(Value = "MiniMax-Hailuo-02")] MiniMaxHailuo02,
    [EnumMember(Value = "I2V-01-Director")] I2V01Director,
    [EnumMember(Value = "I2V-01-live")] I2V01Live,
    [EnumMember(Value = "I2V-01")] I2V01
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxVideoResolution
{
    [EnumMember(Value = "720P")] P720,
    [EnumMember(Value = "768P")] P768,
    [EnumMember(Value = "1080P")] P1080
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxImageToVideoResolution
{
    [EnumMember(Value = "512P")] P512,
    [EnumMember(Value = "720P")] P720,
    [EnumMember(Value = "768P")] P768,
    [EnumMember(Value = "1080P")] P1080
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxStartEndResolution
{
    [EnumMember(Value = "768P")] P768,
    [EnumMember(Value = "1080P")] P1080
}

