using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Recraft;

public static class RecraftTools
{
    [Description("Generate image(s) with Recraft text-to-image and return uploaded resource links.")]
    [McpServerTool(Title = "Generate image with Recraft", Name = "recraft_images_generate", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_ImagesGenerate(
        [Description("Text prompt for image generation.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional model. Defaults to recraftv3.")] RecraftModel? model = RecraftModel.recraftv3,
        [Description("Optional style name.")] string? style = null,
        [Description("Optional style ID (UUID). Cannot be combined with style.")] string? styleId = null,
        [Description("Optional size, e.g. 1024x1024 or 16:9.")] string? size = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftGenerateInput
        {
            Prompt = prompt,
            Model = model ?? RecraftModel.recraftv3,
            Style = style,
            StyleId = styleId,
            Size = size,
            NegativePrompt = negativePrompt,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        ValidateStyleCombination(typed.Style, typed.StyleId);

        var recraft = serviceProvider.GetRequiredService<RecraftClient>();
        var body = new
        {
            prompt = typed.Prompt,
            model = typed.Model.GetEnumMemberValue(),
            style = typed.Style,
            style_id = typed.StyleId,
            size = typed.Size,
            negative_prompt = typed.NegativePrompt,
            n = typed.N,
            response_format = "url"
        };

        var json = await recraft.PostJsonAsync("v1/images/generations", body, cancellationToken);
        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Generate Recraft image variations from an input image URL and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft image-to-image", Name = "recraft_images_image_to_image", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_ImageToImage(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Prompt describing changes.")] string prompt,
        [Description("Strength between 0 and 1.")][Range(0, 1)] double strength,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional model. Defaults to recraftv3.")] RecraftModel? model = RecraftModel.recraftv3,
        [Description("Optional style name.")] string? style = null,
        [Description("Optional style ID (UUID). Cannot be combined with style.")] string? styleId = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftImageToImageInput
        {
            Prompt = prompt,
            Strength = strength,
            Model = model ?? RecraftModel.recraftv3,
            Style = style,
            StyleId = styleId,
            NegativePrompt = negativePrompt,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        ValidateStyleCombination(typed.Style, typed.StyleId);
        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/imageToImage",
            new Dictionary<string, string?>
            {
                ["prompt"] = typed.Prompt,
                ["strength"] = typed.Strength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["model"] = typed.Model.GetEnumMemberValue(),
                ["style"] = typed.Style,
                ["style_id"] = typed.StyleId,
                ["negative_prompt"] = typed.NegativePrompt,
                ["n"] = typed.N.ToString(),
                ["response_format"] = "url"
            },
            new Dictionary<string, FileItem> { ["image"] = imageFile },
            cancellationToken);

        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Inpaint an image with Recraft using an image and mask URL and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft inpaint image", Name = "recraft_images_inpaint", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_Inpaint(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Mask image URL (black/white mask). SharePoint and OneDrive links are supported.")] string maskFileUrl,
        [Description("Prompt describing inpainted content.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional model. Defaults to recraftv3.")] RecraftModel? model = RecraftModel.recraftv3,
        [Description("Optional style name.")] string? style = null,
        [Description("Optional style ID (UUID). Cannot be combined with style.")] string? styleId = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftPromptImageInput
        {
            Prompt = prompt,
            Model = model ?? RecraftModel.recraftv3,
            Style = style,
            StyleId = styleId,
            NegativePrompt = negativePrompt,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        ValidateStyleCombination(typed.Style, typed.StyleId);
        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var maskFile = await DownloadSingleAsync(maskFileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/inpaint",
            new Dictionary<string, string?>
            {
                ["prompt"] = typed.Prompt,
                ["model"] = typed.Model.GetEnumMemberValue(),
                ["style"] = typed.Style,
                ["style_id"] = typed.StyleId,
                ["negative_prompt"] = typed.NegativePrompt,
                ["n"] = typed.N.ToString(),
                ["response_format"] = "url"
            },
            new Dictionary<string, FileItem>
            {
                ["image"] = imageFile,
                ["mask"] = maskFile
            },
            cancellationToken);

        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Replace image background with Recraft and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft replace background", Name = "recraft_images_replace_background", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_ReplaceBackground(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Prompt describing new background.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional model. Defaults to recraftv3.")] RecraftModel? model = RecraftModel.recraftv3,
        [Description("Optional style name.")] string? style = null,
        [Description("Optional style ID (UUID). Cannot be combined with style.")] string? styleId = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftPromptImageInput
        {
            Prompt = prompt,
            Model = model ?? RecraftModel.recraftv3,
            Style = style,
            StyleId = styleId,
            NegativePrompt = negativePrompt,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        ValidateStyleCombination(typed.Style, typed.StyleId);
        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/replaceBackground",
            new Dictionary<string, string?>
            {
                ["prompt"] = typed.Prompt,
                ["model"] = typed.Model.GetEnumMemberValue(),
                ["style"] = typed.Style,
                ["style_id"] = typed.StyleId,
                ["negative_prompt"] = typed.NegativePrompt,
                ["n"] = typed.N.ToString(),
                ["response_format"] = "url"
            },
            new Dictionary<string, FileItem> { ["image"] = imageFile },
            cancellationToken);

        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Generate background for an image with Recraft using an image and mask URL.")]
    [McpServerTool(Title = "Recraft generate background", Name = "recraft_images_generate_background", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_GenerateBackground(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Mask image URL (black/white mask). SharePoint and OneDrive links are supported.")] string maskFileUrl,
        [Description("Prompt describing generated background.")] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional model. Defaults to recraftv3.")] RecraftModel? model = RecraftModel.recraftv3,
        [Description("Optional style name.")] string? style = null,
        [Description("Optional style ID (UUID). Cannot be combined with style.")] string? styleId = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftPromptImageInput
        {
            Prompt = prompt,
            Model = model ?? RecraftModel.recraftv3,
            Style = style,
            StyleId = styleId,
            NegativePrompt = negativePrompt,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        ValidateStyleCombination(typed.Style, typed.StyleId);
        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var maskFile = await DownloadSingleAsync(maskFileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/generateBackground",
            new Dictionary<string, string?>
            {
                ["prompt"] = typed.Prompt,
                ["model"] = typed.Model.GetEnumMemberValue(),
                ["style"] = typed.Style,
                ["style_id"] = typed.StyleId,
                ["negative_prompt"] = typed.NegativePrompt,
                ["n"] = typed.N.ToString(),
                ["response_format"] = "url"
            },
            new Dictionary<string, FileItem>
            {
                ["image"] = imageFile,
                ["mask"] = maskFile
            },
            cancellationToken);

        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Vectorize a raster image with Recraft and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft vectorize image", Name = "recraft_images_vectorize", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_Vectorize(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var outputName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("svg");
        var inputFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/vectorize",
            new Dictionary<string, string?> { ["response_format"] = "url" },
            new Dictionary<string, FileItem> { ["file"] = inputFile },
            cancellationToken);

        return await UploadImageNodeResultAsync(json, outputName, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Remove image background with Recraft and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft remove background", Name = "recraft_images_remove_background", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_RemoveBackground(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var outputName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png");
        var inputFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/removeBackground",
            new Dictionary<string, string?> { ["response_format"] = "url" },
            new Dictionary<string, FileItem> { ["file"] = inputFile },
            cancellationToken);

        return await UploadImageNodeResultAsync(json, outputName, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Apply Recraft crisp upscale and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft crisp upscale", Name = "recraft_images_crisp_upscale", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_CrispUpscale(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var outputName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png");
        var inputFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/crispUpscale",
            new Dictionary<string, string?> { ["response_format"] = "url" },
            new Dictionary<string, FileItem> { ["file"] = inputFile },
            cancellationToken);

        return await UploadImageNodeResultAsync(json, outputName, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Apply Recraft creative upscale and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft creative upscale", Name = "recraft_images_creative_upscale", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_CreativeUpscale(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var outputName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png");
        var inputFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/creativeUpscale",
            new Dictionary<string, string?> { ["response_format"] = "url" },
            new Dictionary<string, FileItem> { ["file"] = inputFile },
            cancellationToken);

        return await UploadImageNodeResultAsync(json, outputName, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Erase region from an image with Recraft using an image and mask URL.")]
    [McpServerTool(Title = "Recraft erase region", Name = "recraft_images_erase_region", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_EraseRegion(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Mask image URL (black/white mask). SharePoint and OneDrive links are supported.")] string maskFileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var outputName = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png");
        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var maskFile = await DownloadSingleAsync(maskFileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/eraseRegion",
            new Dictionary<string, string?> { ["response_format"] = "url" },
            new Dictionary<string, FileItem>
            {
                ["image"] = imageFile,
                ["mask"] = maskFile
            },
            cancellationToken);

        return await UploadImageNodeResultAsync(json, outputName, serviceProvider, requestContext, cancellationToken);
    });

    [Description("Create image variations with Recraft from an input image and return uploaded resource links.")]
    [McpServerTool(Title = "Recraft variate image", Name = "recraft_images_variate", OpenWorld = true, ReadOnly = false, Destructive = false)]
    public static async Task<CallToolResult?> Recraft_VariateImage(
        [Description("Input image URL. SharePoint and OneDrive links are supported.")] string fileUrl,
        [Description("Output size, e.g. 1024x1024 or 16:9.")] string size,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Number of images to generate (1-6).")][Range(1, 6)] int n = 1,
        [Description("Output filename prefix without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        var (typed, _, _) = await requestContext.Server.TryElicit(new RecraftVariateInput
        {
            Size = size,
            N = n,
            Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
        }, cancellationToken);

        var imageFile = await DownloadSingleAsync(fileUrl, serviceProvider, requestContext, cancellationToken);
        var recraft = serviceProvider.GetRequiredService<RecraftClient>();

        var json = await recraft.PostMultipartAsync(
            "v1/images/variateImage",
            new Dictionary<string, string?>
            {
                ["size"] = typed.Size,
                ["n"] = typed.N.ToString(),
                ["response_format"] = "url"
            },
            new Dictionary<string, FileItem> { ["image"] = imageFile },
            cancellationToken);

        return await UploadDataArrayResultAsync(json, typed.Filename, serviceProvider, requestContext, cancellationToken);
    });

    private static void ValidateStyleCombination(string? style, string? styleId)
    {
        if (!string.IsNullOrWhiteSpace(style) && !string.IsNullOrWhiteSpace(styleId))
            throw new ValidationException("style and styleId cannot be used together.");
    }

    private static async Task<FileItem> DownloadSingleAsync(
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        return files.FirstOrDefault() ?? throw new Exception("No file content could be downloaded from fileUrl.");
    }

    private static async Task<CallToolResult?> UploadDataArrayResultAsync(
        JsonNode? json,
        string filenameBase,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();
        var data = json?["data"]?.AsArray() ?? throw new Exception("Recraft response did not include data[].");

        var index = 1;
        foreach (var item in data)
        {
            var url = item?["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var uploaded = await UploadUrlAsResourceAsync(url, filenameBase, index, serviceProvider, requestContext, cancellationToken);
            if (uploaded != null)
                links.Add(uploaded);

            index++;
        }

        if (links.Count == 0)
            throw new Exception("No output URL returned by Recraft.");

        return links.ToResourceLinkCallToolResponse();
    }

    private static async Task<CallToolResult?> UploadImageNodeResultAsync(
        JsonNode? json,
        string filenameBase,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var imageUrl = json?["image"]?["url"]?.ToString();
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new Exception("Recraft response did not include image.url.");

        var uploaded = await UploadUrlAsResourceAsync(imageUrl, filenameBase, 1, serviceProvider, requestContext, cancellationToken);
        return uploaded?.ToResourceLinkCallToolResponse();
    }

    private static async Task<ResourceLinkBlock?> UploadUrlAsResourceAsync(
        string outputUrl,
        string filenameBase,
        int index,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var file = await DownloadSingleAsync(outputUrl, serviceProvider, requestContext, cancellationToken);
        var ext = ResolveExtension(file.MimeType, outputUrl);
        var suffix = index > 1 ? $"-{index}" : string.Empty;

        return await requestContext.Server.Upload(
            serviceProvider,
            $"{filenameBase}{suffix}.{ext}",
            file.Contents,
            cancellationToken);
    }

    private static string ResolveExtension(string? mimeType, string? url)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            var mt = mimeType.ToLowerInvariant();
            if (mt.Contains("svg")) return "svg";
            if (mt.Contains("png")) return "png";
            if (mt.Contains("webp")) return "webp";
            if (mt.Contains("jpeg") || mt.Contains("jpg")) return "jpg";
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var lower = url.ToLowerInvariant();
            if (lower.Contains(".svg")) return "svg";
            if (lower.Contains(".webp")) return "webp";
            if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "jpg";
        }

        return "png";
    }

    [Description("Common request fields for Recraft prompt-based image operations.")]
    public class RecraftPromptImageInput
    {
        [Required]
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = default!;

        [Required]
        [JsonPropertyName("model")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RecraftModel Model { get; set; } = RecraftModel.recraftv3;

        [JsonPropertyName("style")]
        public string? Style { get; set; }

        [JsonPropertyName("styleId")]
        public string? StyleId { get; set; }

        [JsonPropertyName("negativePrompt")]
        public string? NegativePrompt { get; set; }

        [Range(1, 6)]
        [JsonPropertyName("n")]
        public int N { get; set; } = 1;

        [Required]
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = default!;
    }

    [Description("Request fields for Recraft text-to-image generation.")]
    public class RecraftGenerateInput : RecraftPromptImageInput
    {
        [JsonPropertyName("size")]
        public string? Size { get; set; }
    }

    [Description("Request fields for Recraft image-to-image generation.")]
    public class RecraftImageToImageInput : RecraftPromptImageInput
    {
        [Range(0, 1)]
        [JsonPropertyName("strength")]
        public double Strength { get; set; }
    }

    [Description("Request fields for Recraft image variation.")]
    public class RecraftVariateInput
    {
        [Required]
        [JsonPropertyName("size")]
        public string Size { get; set; } = default!;

        [Range(1, 6)]
        [JsonPropertyName("n")]
        public int N { get; set; } = 1;

        [Required]
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = default!;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecraftModel
    {
        [EnumMember(Value = "recraftv2")]
        recraftv2,

        [EnumMember(Value = "recraftv3")]
        recraftv3
    }
}

