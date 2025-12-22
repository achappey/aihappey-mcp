using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AIML.Video;

public static partial class AIMLVideo
{
    public static readonly string MINIMAX_VIDEO_URL = "https://api.aimlapi.com/v2/generate/video/minimax/generation";

    public static async Task<CallToolResult> CheckAndUploadMinimaxAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string generationId,
        string? filename = null,
        bool waitUntilCompleted = false,
        TimeSpan? checkInterval = null,
        CancellationToken cancellationToken = default)
    {
        var settings = serviceProvider.GetRequiredService<AIMLSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        using var client = clientFactory.CreateClient();

        checkInterval ??= TimeSpan.FromSeconds(10);

        var uri = $"https://api.aimlapi.com/v2/generate/video/minimax/generation?generation_id={generationId}";

        // Internal helper: query current generation status
        async Task<(string status, string? url, JsonDocument doc)> QueryStatusAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "unknown"
                : "unknown";

            var url = root.TryGetProperty("video", out var videoElem)
                    && videoElem.ValueKind == JsonValueKind.Object
                    && videoElem.TryGetProperty("url", out var urlElem)
                    ? urlElem.GetString()
                    : null;

            return (status, url, doc);
        }

        // ── Polling loop ──
        (string status, string? url, JsonDocument doc) result;
        do
        {
            result = await QueryStatusAsync();

            if (!waitUntilCompleted || string.Equals(result.status, "completed", StringComparison.OrdinalIgnoreCase))
                break;

            await Task.Delay(checkInterval.Value, cancellationToken);
        }
        while (!cancellationToken.IsCancellationRequested);

        // ── Not yet complete → return status only ──
        if (!string.Equals(result.status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return new CallToolResult
            {
                Content =
                [
                    result.doc.ToJsonContent(uri!),
                    $"⌛ Generation `{generationId}` is **{result.status}**. Please check again later.".ToTextContentBlock()
                ]
            };
        }

        // ── Completed → download + upload ──

        var videoUrl = result.url!;
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, videoUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception("Unable to download video file.");

        var finalName = $"{(string.IsNullOrWhiteSpace(filename) ? generationId : filename)}.mp4";
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            finalName,
            BinaryData.FromBytes(file.Contents.ToArray()),
            cancellationToken);

        // ── Return final link ──
        return new CallToolResult
        {
            Content =
            [
                result.doc.ToJsonContent(uri!),
                uploaded!
            ]
        };
    }

    [Description("Generate a short AI video using MiniMax Hailuo-02. Optionally specify first and last frame images to guide the animation.")]
    [McpServerTool(
        Title = "Generate video with MiniMax Hailuo-02",
        Name = "aiml_video_minimax_hailuo02_generate",
        Destructive = false)]
    public static async Task<CallToolResult?> AIMLVideo_MiniMaxHailuo02Generate(
        [Description("Prompt describing the video content (scene, subject, or action)."), MaxLength(2000)] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional image URL or SharePoint/OneDrive link for the first frame.")] string? firstFrameImage = null,
        [Description("Optional image URL or SharePoint/OneDrive link for the last frame.")] string? lastImageUrl = null,
        [Description("Video duration in seconds (6–10).")] MiniMaxDuration duration = MiniMaxDuration.Seconds6,
        [Description("Video resolution. Default: 768P.")] MiniMaxResolution resolution = MiniMaxResolution.P768,
        [Description("Automatically optimize prompt for better quality (default: true).")] bool promptOptimizer = true,
        [Description("Output filename without extension. Defaults to autogenerated name.")] string? filename = null,
        [Description("If true, the tool waits until the generation is completed before returning.")] bool waitUntilCompleted = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var settings = serviceProvider.GetRequiredService<AIMLSettings>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var aiml = serviceProvider.GetRequiredService<AIMLClient>();
        string? firstFrameData = null;
        string? lastFrameData = null;

        // Step 1: Download first frame (if provided)
        if (!string.IsNullOrWhiteSpace(firstFrameImage))
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, firstFrameImage, cancellationToken);
            var file = files.FirstOrDefault();
            if (file != null)
            {
                firstFrameData = file.ToDataUri();
            }
            else firstFrameData = firstFrameImage;
        }

        // Step 2: Download last frame (if provided)
        if (!string.IsNullOrWhiteSpace(lastImageUrl))
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, lastImageUrl, cancellationToken);
            var file = files.FirstOrDefault();
            if (file != null)
                lastFrameData = file.ToDataUri();
            else lastFrameData = lastImageUrl;
        }

        // Step 3: Ask user for missing info
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new AIMLMiniMaxHailuo02VideoRequest
            {
                Prompt = prompt,
                Duration = duration,
                Resolution = resolution,
                PromptOptimizer = promptOptimizer,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("mp4")
            },
            cancellationToken);
        if (notAccepted != null) return notAccepted;
        if (typed == null) return "User input missing.".ToErrorCallToolResponse();

        // Step 4: Build JSON payload
        var body = new
        {
            model = "minimax/hailuo-02",
            prompt = typed.Prompt,
            first_frame_image = firstFrameData,
            last_image_url = lastFrameData,
            duration = (int)typed.Duration,
            resolution = typed.Resolution.GetEnumMemberValue(),
            prompt_optimizer = typed.PromptOptimizer
        };

        var doc = await aiml.PostAsync(MINIMAX_VIDEO_URL, body, cancellationToken);
        var id = doc.RootElement.TryGetProperty("generation_id", out var idProp)
            ? idProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(id))
            throw new Exception("No generation ID returned from MiniMax Hailuo-02 API.");

        if (waitUntilCompleted)
        {
            return await CheckAndUploadMinimaxAsync(
                serviceProvider,
                requestContext,
                id.Split(":").First(),
                filename,
                waitUntilCompleted: true,
                cancellationToken: cancellationToken);
        }

        // Step 7: Return result
        return doc.ToJsonContent(MINIMAX_VIDEO_URL).ToCallToolResult();
    });
}


// --- DTOs & Enums ---
[Description("Please fill in the MiniMax Hailuo-02 video generation request.")]
public class AIMLMiniMaxHailuo02VideoRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt describing the desired video (scene, subject, or action).")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("duration")]
    [Required]
    [Description("Length of the generated video (6–10 seconds).")]
    public MiniMaxDuration Duration { get; set; } = MiniMaxDuration.Seconds6;

    [JsonPropertyName("resolution")]
    [Required]
    [Description("Video resolution (768P or 1080P).")]
    public MiniMaxResolution Resolution { get; set; } = MiniMaxResolution.P768;

    [JsonPropertyName("prompt_optimizer")]
    [Description("Automatically optimize the prompt (default: true).")]
    public bool PromptOptimizer { get; set; } = true;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxDuration
{
    [EnumMember(Value = "6")]
    Seconds6 = 6,
    [EnumMember(Value = "10")]
    Seconds10 = 10
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MiniMaxResolution
{
    [EnumMember(Value = "768P")]
    P768,
    [EnumMember(Value = "1080P")]
    P1080
}
