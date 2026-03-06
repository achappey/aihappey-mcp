using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Venice;

public static class VeniceImages
{
    private const string GeneratePath = "image/generate";
    private const string UpscalePath = "image/upscale";
    private const string EditPath = "image/edit";
    private const string MultiEditPath = "image/multi-edit";
    private const string BackgroundRemovePath = "image/background-remove";

    [Description("Generate image(s) with Venice AI, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Venice image generation", Name = "venice_images_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt text describing the image to generate.")] string prompt,
        [Description("Model ID. Default: z-image-turbo.")] string model = "z-image-turbo",
        [Description("Output image format: jpeg, png, or webp. Default: webp.")] string format = "webp",
        [Description("Number of images to generate (1-4). Default: 1.")] int variants = 1,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Optional style preset, for example: Cinematic.")] string? style_preset = null,
        [Description("Optional aspect ratio, for example: 1:1 or 16:9.")] string? aspect_ratio = null,
        [Description("Optional resolution for compatible models, for example: 1K, 2K, 4K.")] string? resolution = null,
        [Description("Optional width (1..1280).")]
        int? width = null,
        [Description("Optional height (1..1280).")]
        int? height = null,
        [Description("Optional CFG scale (>0 and <=20).")]
        double? cfg_scale = null,
        [Description("Enable Safe Venice mode. Default: true.")] bool safe_mode = true,
        [Description("Hide watermark if allowed by Venice policy. Default: false.")] bool hide_watermark = false,
        [Description("Embed EXIF metadata in generated images. Default: false.")] bool embed_exif_metadata = false,
        [Description("Enable web search for supported models. Default: false.")] bool enable_web_search = false,
        [Description("Optional seed (-999999999..999999999).")]
        int? seed = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceGenerateImageRequest
                {
                    Prompt = prompt,
                    Model = NormalizeRequired(model, "model"),
                    Format = NormalizeGenerateFormat(format),
                    Variants = variants,
                    NegativePrompt = NormalizeOptional(negative_prompt),
                    StylePreset = NormalizeOptional(style_preset),
                    AspectRatio = NormalizeOptional(aspect_ratio),
                    Resolution = NormalizeOptional(resolution),
                    Width = width,
                    Height = height,
                    CfgScale = cfg_scale,
                    SafeMode = safe_mode,
                    HideWatermark = hide_watermark,
                    EmbedExifMetadata = embed_exif_metadata,
                    EnableWebSearch = enable_web_search,
                    Seed = seed,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerate(typed);

            var body = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["format"] = typed.Format,
                ["variants"] = typed.Variants,
                ["safe_mode"] = typed.SafeMode,
                ["hide_watermark"] = typed.HideWatermark,
                ["embed_exif_metadata"] = typed.EmbedExifMetadata,
                ["enable_web_search"] = typed.EnableWebSearch
            };

            AddIfNotNull(body, "negative_prompt", typed.NegativePrompt);
            AddIfNotNull(body, "style_preset", typed.StylePreset);
            AddIfNotNull(body, "aspect_ratio", typed.AspectRatio);
            AddIfNotNull(body, "resolution", typed.Resolution);
            AddIfNotNull(body, "width", typed.Width);
            AddIfNotNull(body, "height", typed.Height);
            AddIfNotNull(body, "cfg_scale", typed.CfgScale);
            AddIfNotNull(body, "seed", typed.Seed);

            using var client = serviceProvider.CreateVeniceClient(MimeTypes.Json);
            using var req = new HttpRequestMessage(HttpMethod.Post, GeneratePath)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Venice image generation failed ({(int)resp.StatusCode}): {raw}");

            var parsed = JsonNode.Parse(raw)?.AsObject() ?? throw new InvalidOperationException("Venice image generation returned invalid JSON.");
            var images = parsed["images"]?.AsArray() ?? throw new InvalidOperationException("Venice image generation returned no images.");

            var links = new List<ResourceLinkBlock>();
            var ext = "." + typed.Format;
            var index = 0;
            foreach (var image in images)
            {
                var b64 = image?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(b64))
                    continue;

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(b64);
                }
                catch
                {
                    continue;
                }

                index++;
                var uploadName = images.Count == 1
                    ? $"{typed.Filename}{ext}"
                    : $"{typed.Filename}-{index}{ext}";

                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    uploadName,
                    BinaryData.FromBytes(bytes),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }

            if (links.Count == 0)
                throw new InvalidOperationException("Venice image generation succeeded but no images were uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Upscale or enhance an image with Venice AI from a single fileUrl, upload output to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(Title = "Venice image upscale", Name = "venice_images_upscale", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Images_Upscale(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("Upscale factor (1..4). Default: 2.")] double scale = 2,
        [Description("Enable enhancer. Must be true when scale is 1. Default: false.")] bool enhance = false,
        [Description("Optional enhancer creativity (0..1).")]
        double? enhanceCreativity = null,
        [Description("Optional enhancer prompt.")]
        string? enhancePrompt = null,
        [Description("Optional replication strength (0..1).")]
        double? replication = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceUpscaleImageRequest
                {
                    FileUrl = fileUrl,
                    Scale = scale,
                    Enhance = enhance,
                    EnhanceCreativity = enhanceCreativity,
                    EnhancePrompt = NormalizeOptional(enhancePrompt),
                    Replication = replication,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ValidateUpscale(typed);

            var sourceBase64 = await DownloadSingleAsBase64Async(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var body = new JsonObject
            {
                ["image"] = sourceBase64,
                ["scale"] = typed.Scale,
                ["enhance"] = typed.Enhance
            };

            AddIfNotNull(body, "enhanceCreativity", typed.EnhanceCreativity);
            AddIfNotNull(body, "enhancePrompt", typed.EnhancePrompt);
            AddIfNotNull(body, "replication", typed.Replication);

            var bytes = await PostForImageBytesAsync(serviceProvider, UpscalePath, body, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.png",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Edit an image with Venice AI from a single fileUrl, upload output to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(Title = "Venice image edit", Name = "venice_images_edit", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Images_Edit(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("Prompt text describing how to edit the image.")] string prompt,
        [Description("Model ID for edit endpoint. Default: qwen-edit.")] string modelId = "qwen-edit",
        [Description("Optional aspect ratio. Example: auto, 1:1, 16:9.")] string? aspect_ratio = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceEditImageRequest
                {
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    ModelId = NormalizeRequired(modelId, "modelId"),
                    AspectRatio = NormalizeOptional(aspect_ratio),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ValidateEdit(typed);

            var sourceBase64 = await DownloadSingleAsBase64Async(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var body = new JsonObject
            {
                ["image"] = sourceBase64,
                ["prompt"] = typed.Prompt,
                ["modelId"] = typed.ModelId
            };

            AddIfNotNull(body, "aspect_ratio", typed.AspectRatio);

            var bytes = await PostForImageBytesAsync(serviceProvider, EditPath, body, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.png",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Multi-edit images with Venice AI using 1 to 3 comma-separated fileUrl values, upload output to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(Title = "Venice image multi-edit", Name = "venice_images_multi_edit", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Images_MultiEdit(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input image URL list as one comma-separated fileUrl string (1-3 URLs). First image is base image.")] string fileUrl,
        [Description("Prompt text describing the composited edit.")] string prompt,
        [Description("Model ID for multi-edit endpoint. Default: qwen-edit.")] string modelId = "qwen-edit",
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceMultiEditImageRequest
                {
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    ModelId = NormalizeRequired(modelId, "modelId"),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ValidateMultiEdit(typed);

            var urls = ParseUrlList(typed.FileUrl);
            var sourceImages = await DownloadManyAsBase64Async(serviceProvider, requestContext, urls, cancellationToken);

            var imageArray = new JsonArray();
            foreach (var image in sourceImages)
                imageArray.Add(image);

            var body = new JsonObject
            {
                ["images"] = imageArray,
                ["prompt"] = typed.Prompt,
                ["modelId"] = typed.ModelId
            };

            var bytes = await PostForImageBytesAsync(serviceProvider, MultiEditPath, body, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.png",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    [Description("Remove image background with Venice AI from a single fileUrl, upload output to SharePoint/OneDrive, and return only a resource link block.")]
    [McpServerTool(Title = "Venice background remove", Name = "venice_images_background_remove", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Venice_Images_BackgroundRemove(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new VeniceBackgroundRemoveRequest
                {
                    FileUrl = fileUrl,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            if (string.IsNullOrWhiteSpace(typed.FileUrl))
                throw new ValidationException("fileUrl is required.");

            var sourceBase64 = await DownloadSingleAsBase64Async(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var body = new JsonObject
            {
                ["image"] = sourceBase64
            };

            var bytes = await PostForImageBytesAsync(serviceProvider, BackgroundRemovePath, body, cancellationToken);

            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.png",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            return uploaded?.ToResourceLinkCallToolResponse();
        });

    private static async Task<string> DownloadSingleAsBase64Async(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image content from fileUrl.");
        return Convert.ToBase64String(file.Contents.ToArray());
    }

    private static async Task<IReadOnlyList<string>> DownloadManyAsBase64Async(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        IReadOnlyList<string> fileUrls,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        foreach (var url in fileUrls)
            results.Add(await DownloadSingleAsBase64Async(serviceProvider, requestContext, url, cancellationToken));

        return results;
    }

    private static async Task<byte[]> PostForImageBytesAsync(
        IServiceProvider serviceProvider,
        string endpoint,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var client = serviceProvider.CreateVeniceClient("image/png");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            var raw = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Venice image request failed ({(int)resp.StatusCode}): {raw}");
        }

        if (bytes.Length == 0)
            throw new InvalidOperationException("Venice image request returned empty image data.");

        return bytes;
    }

    private static void ValidateGenerate(VeniceGenerateImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ValidationException("prompt is required.");

        if (request.Variants < 1 || request.Variants > 4)
            throw new ValidationException("variants must be between 1 and 4.");

        if (request.Width is < 1 or > 1280)
            throw new ValidationException("width must be between 1 and 1280 when provided.");

        if (request.Height is < 1 or > 1280)
            throw new ValidationException("height must be between 1 and 1280 when provided.");

        if (request.CfgScale is <= 0 or > 20)
            throw new ValidationException("cfg_scale must be > 0 and <= 20 when provided.");

        if (request.Seed is < -999999999 or > 999999999)
            throw new ValidationException("seed must be between -999999999 and 999999999 when provided.");
    }

    private static void ValidateUpscale(VeniceUpscaleImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (request.Scale < 1 || request.Scale > 4)
            throw new ValidationException("scale must be between 1 and 4.");

        if (request.Scale == 1 && !request.Enhance)
            throw new ValidationException("enhance must be true when scale is 1.");

        if (request.Scale > 1 && !request.Enhance && request.EnhanceCreativity is not null)
            throw new ValidationException("enhanceCreativity requires enhance=true.");

        if (request.EnhanceCreativity is < 0 or > 1)
            throw new ValidationException("enhanceCreativity must be between 0 and 1 when provided.");

        if (request.Replication is < 0 or > 1)
            throw new ValidationException("replication must be between 0 and 1 when provided.");
    }

    private static void ValidateEdit(VeniceEditImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ValidationException("prompt is required.");
    }

    private static void ValidateMultiEdit(VeniceMultiEditImageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ValidationException("prompt is required.");

        var urls = ParseUrlList(request.FileUrl);
        if (urls.Count < 1 || urls.Count > 3)
            throw new ValidationException("fileUrl must contain 1 to 3 comma-separated URLs for multi-edit.");
    }

    private static IReadOnlyList<string> ParseUrlList(string? fileUrl)
        => string.IsNullOrWhiteSpace(fileUrl)
            ? []
            : fileUrl
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string NormalizeGenerateFormat(string value)
    {
        var normalized = NormalizeRequired(value, "format").ToLowerInvariant();
        return normalized switch
        {
            "jpeg" or "png" or "webp" => normalized,
            _ => throw new ValidationException("format must be one of: jpeg, png, webp.")
        };
    }

    private static string NormalizeRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{field} is required.");

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfNotNull<T>(JsonObject node, string propertyName, T? value)
    {
        if (value is null)
            return;

        node[propertyName] = JsonValue.Create(value);
    }
}

[Description("Please confirm the Venice image generation request.")]
public sealed class VeniceGenerateImageRequest
{
    [JsonPropertyName("model")]
    [Required]
    [Description("Model ID, for example z-image-turbo.")]
    public string Model { get; set; } = "z-image-turbo";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing the image to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("format")]
    [Description("Output image format: jpeg, png, or webp.")]
    public string Format { get; set; } = "webp";

    [JsonPropertyName("variants")]
    [Description("Number of images to generate (1-4).")]
    public int Variants { get; set; } = 1;

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("style_preset")]
    [Description("Optional style preset.")]
    public string? StylePreset { get; set; }

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("resolution")]
    [Description("Optional resolution for compatible models.")]
    public string? Resolution { get; set; }

    [JsonPropertyName("width")]
    [Description("Optional width (1..1280).")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [Description("Optional height (1..1280).")]
    public int? Height { get; set; }

    [JsonPropertyName("cfg_scale")]
    [Description("Optional CFG scale (>0 and <=20).")]
    public double? CfgScale { get; set; }

    [JsonPropertyName("safe_mode")]
    [Description("Enable Safe Venice mode.")]
    public bool SafeMode { get; set; } = true;

    [JsonPropertyName("hide_watermark")]
    [Description("Hide watermark when policy allows.")]
    public bool HideWatermark { get; set; }

    [JsonPropertyName("embed_exif_metadata")]
    [Description("Embed EXIF metadata.")]
    public bool EmbedExifMetadata { get; set; }

    [JsonPropertyName("enable_web_search")]
    [Description("Enable web search for compatible models.")]
    public bool EnableWebSearch { get; set; }

    [JsonPropertyName("seed")]
    [Description("Optional random seed.")]
    public int? Seed { get; set; }

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Venice image upscale request.")]
public sealed class VeniceUpscaleImageRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("scale")]
    [Description("Upscale factor (1..4).")]
    public double Scale { get; set; } = 2;

    [JsonPropertyName("enhance")]
    [Description("Enable enhancer. Must be true when scale is 1.")]
    public bool Enhance { get; set; }

    [JsonPropertyName("enhanceCreativity")]
    [Description("Optional enhancer creativity (0..1).")]
    public double? EnhanceCreativity { get; set; }

    [JsonPropertyName("enhancePrompt")]
    [Description("Optional enhancer prompt.")]
    public string? EnhancePrompt { get; set; }

    [JsonPropertyName("replication")]
    [Description("Optional replication strength (0..1).")]
    public double? Replication { get; set; }

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Venice image edit request.")]
public sealed class VeniceEditImageRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing how to edit the image.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("modelId")]
    [Required]
    [Description("Model ID for edit endpoint.")]
    public string ModelId { get; set; } = "qwen-edit";

    [JsonPropertyName("aspect_ratio")]
    [Description("Optional aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Venice image multi-edit request.")]
public sealed class VeniceMultiEditImageRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input image URL list as one comma-separated string (1-3 URLs).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing the multi-edit operation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("modelId")]
    [Required]
    [Description("Model ID for multi-edit endpoint.")]
    public string ModelId { get; set; } = "qwen-edit";

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Venice background removal request.")]
public sealed class VeniceBackgroundRemoveRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input image file URL (SharePoint/OneDrive/HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("filename")]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

