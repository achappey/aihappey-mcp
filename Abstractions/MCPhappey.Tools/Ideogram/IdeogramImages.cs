using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Ideogram;

public static class IdeogramImages
{
    private const string ApiBaseUrl = "https://api.ideogram.ai";

    [Description("Generate image(s) with Ideogram 3.0, always confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Generate", Name = "ideogram_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Generate(
        [Description("Prompt for image generation.")][Required] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Resolution value, e.g. 1024x1024.")] string? resolution = null,
        [Description("Aspect ratio, e.g. 1x1, 16x9.")] string? aspectRatio = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPrompt = null,
        [Description("Negative prompt text.")] string? negativePrompt = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Color palette preset name (EMBER, FRESH, etc.).")] string? colorPaletteName = null,
        [Description("Color palette members JSON array (e.g. [{\"color_hex\":\"#ffffff\",\"color_weight\":0.6}]).")] string? colorPaletteMembersJson = null,
        [Description("Style codes, comma-separated (e.g. 1A2B3C4D, ABCDEF12).")]
        string? styleCodes = null,
        [Description("Style type: AUTO, GENERAL, REALISTIC, DESIGN, FICTION.")] string? styleType = null,
        [Description("Style preset value.")] string? stylePreset = null,
        [Description("Style reference image URLs (comma-separated fileUrl values).")]
        string? styleReferenceImageUrls = null,
        [Description("Character reference image URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageUrls = null,
        [Description("Character reference mask URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageMaskUrls = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramGenerateRequest
            {
                Prompt = prompt,
                Seed = seed,
                Resolution = resolution,
                AspectRatio = aspectRatio,
                RenderingSpeed = renderingSpeed,
                MagicPrompt = magicPrompt,
                NegativePrompt = negativePrompt,
                NumImages = numImages,
                ColorPaletteName = colorPaletteName,
                ColorPaletteMembersJson = colorPaletteMembersJson,
                StyleCodes = styleCodes,
                StyleType = styleType,
                StylePreset = stylePreset,
                StyleReferenceImageUrls = styleReferenceImageUrls,
                CharacterReferenceImageUrls = characterReferenceImageUrls,
                CharacterReferenceImageMaskUrls = characterReferenceImageMaskUrls,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidatePalette(typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            ValidateStyleCodeCombination(typed.StyleCodes, typed.StyleReferenceImageUrls, typed.StyleType);

            using var form = new MultipartFormDataContent();
            AddString(form, "prompt", typed.Prompt);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "resolution", typed.Resolution);
            AddString(form, "aspect_ratio", typed.AspectRatio);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "magic_prompt", typed.MagicPrompt);
            AddString(form, "negative_prompt", typed.NegativePrompt);
            AddInt(form, "num_images", typed.NumImages);
            AddColorPalette(form, typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            AddStyleCodes(form, typed.StyleCodes);
            AddString(form, "style_type", typed.StyleType);
            AddString(form, "style_preset", typed.StylePreset);

            await AddFileUrlsAsync(form, "style_reference_images", typed.StyleReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images", typed.CharacterReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images_mask", typed.CharacterReferenceImageMaskUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            ValidateMatchedCounts(typed.CharacterReferenceImageUrls, typed.CharacterReferenceImageMaskUrls, "character_reference_images", "character_reference_images_mask");

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/generate", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Generate transparent background images with Ideogram 3.0, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Generate Transparent", Name = "ideogram_generate_transparent", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_GenerateTransparent(
        [Description("Prompt for image generation.")][Required] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Upscale factor: X1, X2, X4.")] string? upscaleFactor = null,
        [Description("Aspect ratio, e.g. 1x1, 16x9.")] string? aspectRatio = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPrompt = null,
        [Description("Negative prompt text.")] string? negativePrompt = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramGenerateTransparentRequest
            {
                Prompt = prompt,
                Seed = seed,
                UpscaleFactor = upscaleFactor,
                AspectRatio = aspectRatio,
                RenderingSpeed = renderingSpeed,
                MagicPrompt = magicPrompt,
                NegativePrompt = negativePrompt,
                NumImages = numImages,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var form = new MultipartFormDataContent();
            AddString(form, "prompt", typed.Prompt);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "upscale_factor", typed.UpscaleFactor);
            AddString(form, "aspect_ratio", typed.AspectRatio);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "magic_prompt", typed.MagicPrompt);
            AddString(form, "negative_prompt", typed.NegativePrompt);
            AddInt(form, "num_images", typed.NumImages);

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/generate-transparent", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Edit an image with Ideogram 3.0, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Edit", Name = "ideogram_edit", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Edit(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        [Description("Mask image fileUrl.")][Required] string maskFileUrl,
        [Description("Prompt describing the edited result.")][Required] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPrompt = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Style type: AUTO, GENERAL, REALISTIC, DESIGN, FICTION.")] string? styleType = null,
        [Description("Style preset value.")] string? stylePreset = null,
        [Description("Color palette preset name (EMBER, FRESH, etc.).")] string? colorPaletteName = null,
        [Description("Color palette members JSON array.")] string? colorPaletteMembersJson = null,
        [Description("Style codes, comma-separated.")] string? styleCodes = null,
        [Description("Style reference image URLs (comma-separated fileUrl values).")]
        string? styleReferenceImageUrls = null,
        [Description("Character reference image URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageUrls = null,
        [Description("Character reference mask URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageMaskUrls = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramEditRequest
            {
                FileUrl = fileUrl,
                MaskFileUrl = maskFileUrl,
                Prompt = prompt,
                MagicPrompt = magicPrompt,
                NumImages = numImages,
                Seed = seed,
                RenderingSpeed = renderingSpeed,
                StyleType = styleType,
                StylePreset = stylePreset,
                ColorPaletteName = colorPaletteName,
                ColorPaletteMembersJson = colorPaletteMembersJson,
                StyleCodes = styleCodes,
                StyleReferenceImageUrls = styleReferenceImageUrls,
                CharacterReferenceImageUrls = characterReferenceImageUrls,
                CharacterReferenceImageMaskUrls = characterReferenceImageMaskUrls,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidatePalette(typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            ValidateStyleCodeCombination(typed.StyleCodes, typed.StyleReferenceImageUrls, typed.StyleType);

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            var maskFile = await DownloadSingleImageAsync(typed.MaskFileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image", imageFile);
            AddFile(form, "mask", maskFile);

            AddString(form, "prompt", typed.Prompt);
            AddString(form, "magic_prompt", typed.MagicPrompt);
            AddInt(form, "num_images", typed.NumImages);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "style_type", typed.StyleType);
            AddString(form, "style_preset", typed.StylePreset);
            AddColorPalette(form, typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            AddStyleCodes(form, typed.StyleCodes);
            await AddFileUrlsAsync(form, "style_reference_images", typed.StyleReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images", typed.CharacterReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images_mask", typed.CharacterReferenceImageMaskUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            ValidateMatchedCounts(typed.CharacterReferenceImageUrls, typed.CharacterReferenceImageMaskUrls, "character_reference_images", "character_reference_images_mask");

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/edit", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Remix an image with Ideogram 3.0, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Remix", Name = "ideogram_remix", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Remix(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        [Description("Prompt for remix generation.")][Required] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Image weight (0-100).")][Range(0, 100)] int imageWeight = 50,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Resolution value, e.g. 1024x1024.")] string? resolution = null,
        [Description("Aspect ratio, e.g. 1x1, 16x9.")] string? aspectRatio = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPrompt = null,
        [Description("Negative prompt text.")] string? negativePrompt = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Color palette preset name (EMBER, FRESH, etc.).")] string? colorPaletteName = null,
        [Description("Color palette members JSON array.")] string? colorPaletteMembersJson = null,
        [Description("Style codes, comma-separated.")] string? styleCodes = null,
        [Description("Style type: AUTO, GENERAL, REALISTIC, DESIGN, FICTION.")] string? styleType = null,
        [Description("Style preset value.")] string? stylePreset = null,
        [Description("Style reference image URLs (comma-separated fileUrl values).")]
        string? styleReferenceImageUrls = null,
        [Description("Character reference image URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageUrls = null,
        [Description("Character reference mask URLs (comma-separated fileUrl values).")]
        string? characterReferenceImageMaskUrls = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramRemixRequest
            {
                FileUrl = fileUrl,
                Prompt = prompt,
                ImageWeight = imageWeight,
                Seed = seed,
                Resolution = resolution,
                AspectRatio = aspectRatio,
                RenderingSpeed = renderingSpeed,
                MagicPrompt = magicPrompt,
                NegativePrompt = negativePrompt,
                NumImages = numImages,
                ColorPaletteName = colorPaletteName,
                ColorPaletteMembersJson = colorPaletteMembersJson,
                StyleCodes = styleCodes,
                StyleType = styleType,
                StylePreset = stylePreset,
                StyleReferenceImageUrls = styleReferenceImageUrls,
                CharacterReferenceImageUrls = characterReferenceImageUrls,
                CharacterReferenceImageMaskUrls = characterReferenceImageMaskUrls,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidatePalette(typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            ValidateStyleCodeCombination(typed.StyleCodes, typed.StyleReferenceImageUrls, typed.StyleType);
            ValidateImageWeight(typed.ImageWeight);

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image", imageFile);
            AddString(form, "prompt", typed.Prompt);
            AddInt(form, "image_weight", typed.ImageWeight);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "resolution", typed.Resolution);
            AddString(form, "aspect_ratio", typed.AspectRatio);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "magic_prompt", typed.MagicPrompt);
            AddString(form, "negative_prompt", typed.NegativePrompt);
            AddInt(form, "num_images", typed.NumImages);
            AddColorPalette(form, typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            AddStyleCodes(form, typed.StyleCodes);
            AddString(form, "style_type", typed.StyleType);
            AddString(form, "style_preset", typed.StylePreset);
            await AddFileUrlsAsync(form, "style_reference_images", typed.StyleReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images", typed.CharacterReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            await AddFileUrlsAsync(form, "character_reference_images_mask", typed.CharacterReferenceImageMaskUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);
            ValidateMatchedCounts(typed.CharacterReferenceImageUrls, typed.CharacterReferenceImageMaskUrls, "character_reference_images", "character_reference_images_mask");

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/remix", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Reframe an image with Ideogram 3.0, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Reframe", Name = "ideogram_reframe", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Reframe(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        [Description("Resolution value, e.g. 1024x1024.")][Required] string resolution,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Style preset value.")] string? stylePreset = null,
        [Description("Color palette preset name (EMBER, FRESH, etc.).")] string? colorPaletteName = null,
        [Description("Color palette members JSON array.")] string? colorPaletteMembersJson = null,
        [Description("Style codes, comma-separated.")] string? styleCodes = null,
        [Description("Style reference image URLs (comma-separated fileUrl values).")]
        string? styleReferenceImageUrls = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramReframeRequest
            {
                FileUrl = fileUrl,
                Resolution = resolution,
                NumImages = numImages,
                Seed = seed,
                RenderingSpeed = renderingSpeed,
                StylePreset = stylePreset,
                ColorPaletteName = colorPaletteName,
                ColorPaletteMembersJson = colorPaletteMembersJson,
                StyleCodes = styleCodes,
                StyleReferenceImageUrls = styleReferenceImageUrls,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidatePalette(typed.ColorPaletteName, typed.ColorPaletteMembersJson);

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image", imageFile);
            AddString(form, "resolution", typed.Resolution);
            AddInt(form, "num_images", typed.NumImages);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "style_preset", typed.StylePreset);
            AddColorPalette(form, typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            AddStyleCodes(form, typed.StyleCodes);
            await AddFileUrlsAsync(form, "style_reference_images", typed.StyleReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/reframe", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Replace image background with Ideogram 3.0, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Replace Background", Name = "ideogram_replace_background", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_ReplaceBackground(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        [Description("Prompt describing the desired new background.")][Required] string prompt,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPrompt = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Rendering speed: FLASH, TURBO, DEFAULT, QUALITY.")] string? renderingSpeed = null,
        [Description("Style preset value.")] string? stylePreset = null,
        [Description("Color palette preset name (EMBER, FRESH, etc.).")] string? colorPaletteName = null,
        [Description("Color palette members JSON array.")] string? colorPaletteMembersJson = null,
        [Description("Style codes, comma-separated.")] string? styleCodes = null,
        [Description("Style reference image URLs (comma-separated fileUrl values).")]
        string? styleReferenceImageUrls = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramReplaceBackgroundRequest
            {
                FileUrl = fileUrl,
                Prompt = prompt,
                MagicPrompt = magicPrompt,
                NumImages = numImages,
                Seed = seed,
                RenderingSpeed = renderingSpeed,
                StylePreset = stylePreset,
                ColorPaletteName = colorPaletteName,
                ColorPaletteMembersJson = colorPaletteMembersJson,
                StyleCodes = styleCodes,
                StyleReferenceImageUrls = styleReferenceImageUrls,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidatePalette(typed.ColorPaletteName, typed.ColorPaletteMembersJson);

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image", imageFile);
            AddString(form, "prompt", typed.Prompt);
            AddString(form, "magic_prompt", typed.MagicPrompt);
            AddInt(form, "num_images", typed.NumImages);
            AddInt(form, "seed", typed.Seed);
            AddString(form, "rendering_speed", typed.RenderingSpeed);
            AddString(form, "style_preset", typed.StylePreset);
            AddColorPalette(form, typed.ColorPaletteName, typed.ColorPaletteMembersJson);
            AddStyleCodes(form, typed.StyleCodes);
            await AddFileUrlsAsync(form, "style_reference_images", typed.StyleReferenceImageUrls, serviceProvider, requestContext, cancellationToken, 10 * 1024 * 1024);

            var response = await PostMultipartAsync(serviceProvider, "/v1/ideogram-v3/replace-background", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Upscale an image with Ideogram, confirm via elicitation, upload results, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Upscale", Name = "ideogram_upscale", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Upscale(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional prompt to guide upscale.")] string? prompt = null,
        [Description("Resemblance (0-100).")][Range(0, 100)] int? resemblance = null,
        [Description("Detail (0-100).")][Range(0, 100)] int? detail = null,
        [Description("Magic prompt option: AUTO, ON, OFF.")] string? magicPromptOption = null,
        [Description("Number of images to generate.")] int numImages = 1,
        [Description("Random seed for reproducibility.")] int? seed = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramUpscaleRequest
            {
                FileUrl = fileUrl,
                Prompt = prompt,
                Resemblance = resemblance,
                Detail = detail,
                MagicPromptOption = magicPromptOption,
                NumImages = numImages,
                Seed = seed,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image_file", imageFile);
            form.Add(new StringContent(BuildUpscaleRequestJson(typed), Encoding.UTF8, "application/json"), "image_request");

            var response = await PostMultipartAsync(serviceProvider, "/upscale", form, cancellationToken);
            var urls = ExtractImageUrls(response);
            return await UploadUrlsAsync(urls, typed.Filename, serviceProvider, requestContext, cancellationToken);
        });

    [Description("Describe an image with Ideogram, confirm via elicitation, upload text output, and return only resource link blocks.")]
    [McpServerTool(Title = "Ideogram Describe", Name = "ideogram_describe", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Ideogram_Describe(
        [Description("Input image fileUrl.")][Required] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Describe model version: V_2 or V_3.")] string? describeModelVersion = null,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new IdeogramDescribeRequest
            {
                FileUrl = fileUrl,
                DescribeModelVersion = describeModelVersion,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            using var form = new MultipartFormDataContent();
            var imageFile = await DownloadSingleImageAsync(typed.FileUrl, serviceProvider, requestContext, cancellationToken);
            AddFile(form, "image_file", imageFile);
            AddString(form, "describe_model_version", typed.DescribeModelVersion);

            var response = await PostMultipartAsync(serviceProvider, "/describe", form, cancellationToken);
            var descriptionText = ExtractDescribeText(response);

            var outputName = $"{typed.Filename}.txt";
            var upload = await requestContext.Server.Upload(
                serviceProvider,
                outputName,
                BinaryData.FromString(descriptionText),
                cancellationToken);

            return upload?.ToResourceLinkCallToolResponse();
        });

    private static async Task<JsonNode> PostMultipartAsync(
        IServiceProvider serviceProvider,
        string path,
        MultipartFormDataContent form,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetService<IdeogramSettings>()
            ?? throw new InvalidOperationException("IdeogramSettings not configured. Provide Api-Key for api.ideogram.ai.");

        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = form
        };

        request.Headers.TryAddWithoutValidation("Api-Key", settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {raw}");

        return JsonNode.Parse(raw) ?? new JsonObject();
    }

    private static async Task<CallToolResult?> UploadUrlsAsync(
        IEnumerable<string> urls,
        string filenameBase,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var index = 0;

        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            index++;
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null) continue;

            var ext = ResolveImageExtension(file.MimeType, url);
            var suffix = index > 1 ? $"-{index}" : string.Empty;
            var upload = await requestContext.Server.Upload(
                serviceProvider,
                $"{filenameBase}{suffix}.{ext}",
                file.Contents,
                cancellationToken);

            if (upload != null) links.Add(upload);
        }

        return links.Count > 0 ? links.ToResourceLinkCallToolResponse() : null;
    }

    private static List<string> ExtractImageUrls(JsonNode? response)
    {
        var urls = new List<string>();
        var data = response?["data"] as JsonArray;
        if (data == null) return urls;

        foreach (var item in data)
        {
            var url = item?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
        }

        return urls;
    }

    private static string ExtractDescribeText(JsonNode? response)
    {
        var descriptions = response?["descriptions"] as JsonArray;
        if (descriptions == null || descriptions.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var item in descriptions)
        {
            var text = item?["text"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(text);
        }

        return sb.ToString();
    }

    private static async Task<FileItem> DownloadSingleImageAsync(
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("No file content could be downloaded from fileUrl.");
        ValidateImageFile(file, 10 * 1024 * 1024);
        return file;
    }

    private static async Task AddFileUrlsAsync(
        MultipartFormDataContent form,
        string fieldName,
        string? fileUrls,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken,
        long maxTotalBytes)
    {
        if (string.IsNullOrWhiteSpace(fileUrls)) return;

        var urls = SplitCsv(fileUrls);
        if (urls.Count == 0) return;

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        long totalBytes = 0;

        foreach (var url in urls)
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null) continue;

            ValidateImageFile(file, 10 * 1024 * 1024);
            totalBytes += file.Contents.ToArray().Length;
            if (totalBytes > maxTotalBytes)
                throw new ValidationException($"Total size for {fieldName} exceeds {maxTotalBytes / (1024 * 1024)}MB.");

            AddFile(form, fieldName, file);
        }
    }

    private static void AddFile(MultipartFormDataContent form, string fieldName, FileItem file)
    {
        var content = new ByteArrayContent(file.Contents.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
        var filename = string.IsNullOrWhiteSpace(file.Filename)
            ? $"{fieldName}.{ResolveImageExtension(file.MimeType, null)}"
            : file.Filename;
        form.Add(content, fieldName, filename);
    }

    private static void AddString(MultipartFormDataContent form, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        form.Add(new StringContent(value), name);
    }

    private static void AddInt(MultipartFormDataContent form, string name, int? value)
    {
        if (!value.HasValue) return;
        form.Add(new StringContent(value.Value.ToString()), name);
    }

    private static void AddColorPalette(MultipartFormDataContent form, string? name, string? membersJson)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(membersJson)) return;

        JsonObject payload;
        if (!string.IsNullOrWhiteSpace(name))
        {
            payload = new JsonObject
            {
                ["name"] = name
            };
        }
        else
        {
            var membersNode = JsonNode.Parse(membersJson!) as JsonArray
                ?? throw new ValidationException("colorPaletteMembersJson must be a JSON array.");
            payload = new JsonObject
            {
                ["members"] = membersNode
            };
        }

        form.Add(new StringContent(payload.ToJsonString()), "color_palette");
    }

    private static void AddStyleCodes(MultipartFormDataContent form, string? styleCodes)
    {
        if (string.IsNullOrWhiteSpace(styleCodes)) return;
        foreach (var code in SplitCsv(styleCodes))
        {
            form.Add(new StringContent(code), "style_codes");
        }
    }

    private static void ValidatePalette(string? name, string? membersJson)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(membersJson))
            throw new ValidationException("Only one of colorPaletteName or colorPaletteMembersJson can be set.");
    }

    private static void ValidateStyleCodeCombination(string? styleCodes, string? styleReferenceImageUrls, string? styleType)
    {
        if (string.IsNullOrWhiteSpace(styleCodes)) return;
        if (!string.IsNullOrWhiteSpace(styleReferenceImageUrls))
            throw new ValidationException("style_codes cannot be used with style_reference_images.");
        if (!string.IsNullOrWhiteSpace(styleType))
            throw new ValidationException("style_codes cannot be used with style_type.");
    }

    private static void ValidateMatchedCounts(string? leftUrls, string? rightUrls, string leftName, string rightName)
    {
        if (string.IsNullOrWhiteSpace(leftUrls) || string.IsNullOrWhiteSpace(rightUrls)) return;
        var leftCount = SplitCsv(leftUrls).Count;
        var rightCount = SplitCsv(rightUrls).Count;
        if (leftCount != rightCount)
            throw new ValidationException($"{rightName} count must match {leftName} count.");
    }

    private static void ValidateImageFile(FileItem file, long maxBytes)
    {
        var size = file.Contents.ToArray().Length;
        if (size > maxBytes)
            throw new ValidationException($"Image exceeds {maxBytes / (1024 * 1024)}MB limit.");

        var mime = file.MimeType?.ToLowerInvariant() ?? string.Empty;
        if (!mime.StartsWith("image/"))
            throw new ValidationException("Only image files are supported.");

        if (!mime.Contains("jpeg") && !mime.Contains("jpg") && !mime.Contains("png") && !mime.Contains("webp"))
            throw new ValidationException("Supported image formats are JPEG, PNG, and WebP.");
    }

    private static List<string> SplitCsv(string value)
        => [.. value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))];

    private static string ResolveImageExtension(string? mimeType, string? url)
    {
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            var mt = mimeType.ToLowerInvariant();
            if (mt.Contains("png")) return "png";
            if (mt.Contains("webp")) return "webp";
            if (mt.Contains("jpeg") || mt.Contains("jpg")) return "jpg";
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var lower = url.ToLowerInvariant();
            if (lower.Contains(".webp")) return "webp";
            if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "jpg";
        }

        return "png";
    }

    private static string BuildUpscaleRequestJson(IdeogramUpscaleRequest input)
    {
        var payload = new JsonObject();
        if (!string.IsNullOrWhiteSpace(input.Prompt)) payload["prompt"] = input.Prompt;
        if (input.Resemblance.HasValue) payload["resemblance"] = input.Resemblance.Value;
        if (input.Detail.HasValue) payload["detail"] = input.Detail.Value;
        if (!string.IsNullOrWhiteSpace(input.MagicPromptOption)) payload["magic_prompt_option"] = input.MagicPromptOption;
        if (input.NumImages > 0) payload["num_images"] = input.NumImages;
        if (input.Seed.HasValue) payload["seed"] = input.Seed.Value;
        return payload.ToJsonString();
    }

    private static void ValidateImageWeight(int value)
    {
        if (value < 0 || value > 100)
            throw new ValidationException("imageWeight must be between 0 and 100.");
    }

    [Description("Please confirm the Ideogram generate request details.")]
    public sealed class IdeogramGenerateRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }

        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("magic_prompt")]
        public string? MagicPrompt { get; set; }

        [JsonPropertyName("negative_prompt")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("colorPaletteName")]
        public string? ColorPaletteName { get; set; }

        [JsonPropertyName("colorPaletteMembersJson")]
        public string? ColorPaletteMembersJson { get; set; }

        [JsonPropertyName("style_codes")]
        public string? StyleCodes { get; set; }

        [JsonPropertyName("style_type")]
        public string? StyleType { get; set; }

        [JsonPropertyName("style_preset")]
        public string? StylePreset { get; set; }

        [JsonPropertyName("style_reference_images")]
        public string? StyleReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images")]
        public string? CharacterReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images_mask")]
        public string? CharacterReferenceImageMaskUrls { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;

    }

    [Description("Please confirm the Ideogram transparent generation request details.")]
    public sealed class IdeogramGenerateTransparentRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("upscale_factor")]
        public string? UpscaleFactor { get; set; }

        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("magic_prompt")]
        public string? MagicPrompt { get; set; }

        [JsonPropertyName("negative_prompt")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;

    }

    [Description("Please confirm the Ideogram edit request details.")]
    public sealed class IdeogramEditRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("maskFileUrl")]
        [Required]
        public string MaskFileUrl { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Required]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("magic_prompt")]
        public string? MagicPrompt { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("style_type")]
        public string? StyleType { get; set; }

        [JsonPropertyName("style_preset")]
        public string? StylePreset { get; set; }

        [JsonPropertyName("colorPaletteName")]
        public string? ColorPaletteName { get; set; }

        [JsonPropertyName("colorPaletteMembersJson")]
        public string? ColorPaletteMembersJson { get; set; }

        [JsonPropertyName("style_codes")]
        public string? StyleCodes { get; set; }

        [JsonPropertyName("style_reference_images")]
        public string? StyleReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images")]
        public string? CharacterReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images_mask")]
        public string? CharacterReferenceImageMaskUrls { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;
    }

    [Description("Please confirm the Ideogram remix request details.")]
    public sealed class IdeogramRemixRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Required]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("image_weight")]
        [Range(0, 100)]
        public int ImageWeight { get; set; } = 50;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }

        [JsonPropertyName("aspect_ratio")]
        public string? AspectRatio { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("magic_prompt")]
        public string? MagicPrompt { get; set; }

        [JsonPropertyName("negative_prompt")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("colorPaletteName")]
        public string? ColorPaletteName { get; set; }

        [JsonPropertyName("colorPaletteMembersJson")]
        public string? ColorPaletteMembersJson { get; set; }

        [JsonPropertyName("style_codes")]
        public string? StyleCodes { get; set; }

        [JsonPropertyName("style_type")]
        public string? StyleType { get; set; }

        [JsonPropertyName("style_preset")]
        public string? StylePreset { get; set; }

        [JsonPropertyName("style_reference_images")]
        public string? StyleReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images")]
        public string? CharacterReferenceImageUrls { get; set; }

        [JsonPropertyName("character_reference_images_mask")]
        public string? CharacterReferenceImageMaskUrls { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;


    }

    [Description("Please confirm the Ideogram reframe request details.")]
    public sealed class IdeogramReframeRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("resolution")]
        [Required]
        public string Resolution { get; set; } = default!;

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("style_preset")]
        public string? StylePreset { get; set; }

        [JsonPropertyName("colorPaletteName")]
        public string? ColorPaletteName { get; set; }

        [JsonPropertyName("colorPaletteMembersJson")]
        public string? ColorPaletteMembersJson { get; set; }

        [JsonPropertyName("style_codes")]
        public string? StyleCodes { get; set; }

        [JsonPropertyName("style_reference_images")]
        public string? StyleReferenceImageUrls { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;

    }

    [Description("Please confirm the Ideogram replace background request details.")]
    public sealed class IdeogramReplaceBackgroundRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Required]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("magic_prompt")]
        public string? MagicPrompt { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("rendering_speed")]
        public string? RenderingSpeed { get; set; }

        [JsonPropertyName("style_preset")]
        public string? StylePreset { get; set; }

        [JsonPropertyName("colorPaletteName")]
        public string? ColorPaletteName { get; set; }

        [JsonPropertyName("colorPaletteMembersJson")]
        public string? ColorPaletteMembersJson { get; set; }

        [JsonPropertyName("style_codes")]
        public string? StyleCodes { get; set; }

        [JsonPropertyName("style_reference_images")]
        public string? StyleReferenceImageUrls { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;
    }

    [Description("Please confirm the Ideogram upscale request details.")]
    public sealed class IdeogramUpscaleRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("resemblance")]
        [Range(0, 100)]
        public int? Resemblance { get; set; }

        [JsonPropertyName("detail")]
        [Range(0, 100)]
        public int? Detail { get; set; }

        [JsonPropertyName("magic_prompt_option")]
        public string? MagicPromptOption { get; set; }

        [JsonPropertyName("num_images")]
        public int NumImages { get; set; } = 1;

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;

    }

    [Description("Please confirm the Ideogram describe request details.")]
    public sealed class IdeogramDescribeRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("describe_model_version")]
        public string? DescribeModelVersion { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        public string Filename { get; set; } = default!;

    }
}
