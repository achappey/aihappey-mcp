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

namespace MCPhappey.Tools.Runway.Images;

public static class RunwayImages
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RunwayImageModel
    {
        [EnumMember(Value = "gen4_image_turbo")]
        Gen4ImageTurbo,

        [EnumMember(Value = "gen4_image")]
        Gen4Image,

        [EnumMember(Value = "gemini_2.5_flash")]
        Gemini25Flash
    }

    // ---------- TEXT → IMAGE ----------
    [Description("Create an image from text using Runway. Returns image URL or resource block.")]
    [McpServerTool(Title = "Create Runway Text-to-Image", Name = "runway_text_to_image", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_TextToImage(
        [MaxLength(1000)] string promptText,
        RunwayImageModel? model,
        [Description("Output resolution of the generated image. " +
        "For gen4_image_turbo and gen4_image: 1920:1080, 1080:1920, 1024:1024, 1360:768, 1080:1080, 1168:880, " +
        "1440:1080, 1080:1440, 1808:768, 2112:912, 1280:720, 720:1280, 720:720, 960:720, 720:960, 1680:720. " +
        "For gemini_2.5_flash: 1344:768, 768:1344, 1024:1024, 1184:864, 864:1184, 1536:672, 832:1248, " +
        "1248:832, 896:1152, 1152:896.")] string? ratio,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        int? seed = null,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewTextToImage
        {
            PromptText = promptText,
            Model = model ?? RunwayImageModel.Gen4ImageTurbo,
            Ratio = string.IsNullOrWhiteSpace(ratio) ? "1:1" : ratio!,
            Seed = seed
        }, ct);

        var runway = sp.GetRequiredService<RunwayClient>();
        var payload = new
        {
            promptText = typed.PromptText,
            model = typed.Model.GetEnumMemberValue(),
            ratio = typed.Ratio,
            seed = typed.Seed
        };
        var json = await runway.TextToImageAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "png", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });

    // ---------- IMAGE → IMAGE ----------
    [Description("Transform an input image into a new image using Runway. Returns task id or image URL.")]
    [McpServerTool(Title = "Create Runway Image-to-Image", Name = "runway_image_to_image", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Runway_ImageToImage(
        IEnumerable<string> promptImages,
        string? promptText,
        RunwayImageModel? model,
        [Description("Output resolution of the generated image. " +
        "For gen4_image_turbo and gen4_image: 1920:1080, 1080:1920, 1024:1024, 1360:768, 1080:1080, 1168:880, " +
        "1440:1080, 1080:1440, 1808:768, 2112:912, 1280:720, 720:1280, 720:720, 960:720, 720:960, 1680:720. " +
        "For gemini_2.5_flash: 1344:768, 768:1344, 1024:1024, 1184:864, 864:1184, 1536:672, 832x1248, " +
        "1248x832, 896x1152, 1152x896.")] string? ratio,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        [Description("Wait until the fully completed and upload outputs.")]
        bool? waitUntilCompleted,
        int? seed,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
    {
        if (promptImages == null || !promptImages.Any())
            throw new ValidationException("At least one prompt image is required.");

        var (typed, _, _) = await rc.Server.TryElicit(new RunwayNewImageToImage
        {
            PromptText = promptText,
            Model = model ?? RunwayImageModel.Gen4ImageTurbo,
            Ratio = string.IsNullOrWhiteSpace(ratio) ? "1:1" : ratio!,
            Seed = seed
        }, ct);

        var downloadService = sp.GetRequiredService<DownloadService>();
        var runway = sp.GetRequiredService<RunwayClient>();

        // convert input images to base64 data URIs
        var imagePayloads = new List<object>();
        foreach (var img in promptImages)
        {
            var files = await downloadService.DownloadContentAsync(sp, rc.Server, img, ct);
            var bytes = files.FirstOrDefault() ?? throw new Exception($"Failed to download image: {img}");
            imagePayloads.Add(new { uri = bytes.ToDataUri() });
        }

        var payload = new
        {
            referenceImages = imagePayloads,
            promptText = typed.PromptText,
            model = typed.Model.GetEnumMemberValue(),
            ratio = typed.Ratio,
            seed = typed.Seed
        };

        var json = await runway.TextToImageAsync(payload, ct);
        var taskId = runway.ExtractTaskId(json);

        // 5️⃣ Handle completion mode
        if (waitUntilCompleted == true)
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, "png", ct);

        // Return only task ID if waiting disabled
        return runway.CreateImmediateTaskResult(taskId);
    });



    // ---------- DTOs ----------
    [Description("Typed input for Runway text-to-image generation.")]
    public class RunwayNewTextToImage
    {
        [JsonPropertyName("promptText")]
        [Required]
        [Description("Text prompt describing the desired image (up to 1000 characters).")]
        public string PromptText { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Model variant.")]
        public RunwayImageModel Model { get; set; } = default!;

        [JsonPropertyName("ratio")]
        [Required]
        [Description("Aspect ratio, e.g., 1:1, 16:9, 9:16, 4:3, 3:4.")]
        public string Ratio { get; set; } = default!;

        [JsonPropertyName("seed")]
        [Description("Optional integer seed for reproducibility.")]
        [Range(0, int.MaxValue)]
        public int? Seed { get; set; }
    }

    [Description("Typed input for Runway image-to-image generation.")]
    public class RunwayNewImageToImage
    {
        [JsonPropertyName("promptText")]
        [Description("Optional text prompt refining the transformation.")]
        public string? PromptText { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Model variant.")]
        public RunwayImageModel Model { get; set; } = default!;

        [JsonPropertyName("ratio")]
        [Required]
        [Description("Aspect ratio, e.g., 1:1, 16:9, 9:16, 4:3, 3:4.")]
        public string Ratio { get; set; } = default!;

        [JsonPropertyName("seed")]
        [Description("Optional integer seed for reproducibility.")]
        [Range(0, int.MaxValue)]
        public int? Seed { get; set; }
    }
}