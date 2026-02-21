using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Infomaniak;

public static class InfomaniakImages
{
    private const string ApiBaseUrl = "https://api.infomaniak.com";

    [Description("Generate image(s) with Infomaniak OpenAI-compatible API, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "Infomaniak Image Generation",
        Name = "infomaniak_images_generate",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Infomaniak_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt text describing the image(s) to generate.")] string prompt,
        [Description("Model name to use.")] string model = "sdxl_lightning",
        [Description("Number of images to generate (1-5).")][Range(1, 5)] int n = 1,
        [Description("Optional negative prompt (not supported by all models).")]
        string? negative_prompt = null,
        [Description("Image quality: standard or hd.")] string quality = "standard",
        [Description("Response format. Currently only b64_json is supported.")] string response_format = "b64_json",
        [Description("Image size: 1024x1024, 1024x1792, or 1792x1024.")] string size = "1024x1024",
        [Description("Optional style.")] string? style = null,
        [Description("Sync mode; false by default.")] bool sync = false,
        [Description("Infomaniak AI product id. If omitted, tries x-infomaniak-product-id from headers.")]
        int? productId = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<InfomaniakSettings>();
            var resolvedProductId = productId ?? settings.DefaultProductId
                ?? throw new ValidationException("Missing productId. Provide it explicitly or configure x-infomaniak-product-id header.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new InfomaniakImageGenerateRequest
                {
                    ProductId = resolvedProductId,
                    Prompt = prompt,
                    Model = model,
                    N = n,
                    NegativePrompt = negative_prompt,
                    Quality = quality,
                    ResponseFormat = response_format,
                    Size = size,
                    Style = style,
                    Sync = sync,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Confirmation = "GENERATE"
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (!string.Equals(typed.Confirmation?.Trim(), "GENERATE", StringComparison.OrdinalIgnoreCase))
                return "Image generation canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidateImageGenerationRequest(typed);

            var payload = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["n"] = typed.N,
                ["quality"] = typed.Quality,
                ["response_format"] = typed.ResponseFormat,
                ["size"] = typed.Size,
                ["sync"] = typed.Sync
            };

            if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                payload["negative_prompt"] = typed.NegativePrompt;

            if (!string.IsNullOrWhiteSpace(typed.Style))
                payload["style"] = typed.Style;

            var response = await PostInfomaniakAsync(serviceProvider, $"/1/ai/{typed.ProductId}/openai/images/generations", payload, cancellationToken);
            var links = await UploadImagesFromB64Async(serviceProvider, requestContext, response, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Image generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Generate photo-realistic personalized images with Infomaniak PhotoMaker from a single input fileUrl, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "Infomaniak PhotoMaker",
        Name = "infomaniak_images_photo_maker",
        Destructive = false,
        ReadOnly = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Infomaniak_Images_PhotoMaker(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Single input image URL (SharePoint/OneDrive/public HTTP) for identity conditioning.")] string fileUrl,
        [Description("Prompt text. Use trigger word `img` after class word (e.g., woman img).")]
        string prompt,
        [Description("Number of images to generate (1-5).")][Range(1, 5)] int n = 1,
        [Description("Optional prompt weighting guidance scale.")] double? guidance_scale = null,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Image quality: standard or hd.")] string quality = "standard",
        [Description("Response format. Currently only b64_json is supported.")] string response_format = "b64_json",
        [Description("Image size: 1024x1024, 1024x1792, or 1792x1024.")] string size = "1024x1024",
        [Description("Optional style.")] string? style = null,
        [Description("Optional style strength ratio (10-50).")][Range(10, 50)] int? style_strength_ratio = null,
        [Description("Sync mode; false by default.")] bool sync = false,
        [Description("Infomaniak AI product id. If omitted, tries x-infomaniak-product-id from headers.")]
        int? productId = null,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var settings = serviceProvider.GetRequiredService<InfomaniakSettings>();
            var resolvedProductId = productId ?? settings.DefaultProductId
                ?? throw new ValidationException("Missing productId. Provide it explicitly or configure x-infomaniak-product-id header.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new InfomaniakPhotoMakerRequest
                {
                    ProductId = resolvedProductId,
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    N = n,
                    GuidanceScale = guidance_scale,
                    NegativePrompt = negative_prompt,
                    Quality = quality,
                    ResponseFormat = response_format,
                    Size = size,
                    Style = style,
                    StyleStrengthRatio = style_strength_ratio,
                    Sync = sync,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Confirmation = "GENERATE"
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (!string.Equals(typed.Confirmation?.Trim(), "GENERATE", StringComparison.OrdinalIgnoreCase))
                return "PhotoMaker generation canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidatePhotoMakerRequest(typed);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source image content from fileUrl.");
            var imageBase64 = Convert.ToBase64String(file.Contents.ToArray());

            var payload = new JsonObject
            {
                ["images"] = new JsonArray(imageBase64),
                ["prompt"] = typed.Prompt,
                ["n"] = typed.N,
                ["quality"] = typed.Quality,
                ["response_format"] = typed.ResponseFormat,
                ["size"] = typed.Size,
                ["sync"] = typed.Sync
            };

            if (typed.GuidanceScale.HasValue)
                payload["guidance_scale"] = typed.GuidanceScale.Value;

            if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                payload["negative_prompt"] = typed.NegativePrompt;

            if (!string.IsNullOrWhiteSpace(typed.Style))
                payload["style"] = typed.Style;

            if (typed.StyleStrengthRatio.HasValue)
                payload["style_strength_ratio"] = typed.StyleStrengthRatio.Value;

            var response = await PostInfomaniakAsync(serviceProvider, $"/1/ai/{typed.ProductId}/images/generations/photo_maker", payload, cancellationToken);
            var links = await UploadImagesFromB64Async(serviceProvider, requestContext, response, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("PhotoMaker generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<JsonNode> PostInfomaniakAsync(
        IServiceProvider serviceProvider,
        string path,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<InfomaniakSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {raw}");

        return JsonNode.Parse(raw) ?? new JsonObject();
    }

    private static async Task<List<ResourceLinkBlock>> UploadImagesFromB64Async(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        JsonNode response,
        string fileName,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();
        var data = response["data"]?.AsArray();
        if (data == null || data.Count == 0)
            return links;

        var i = 0;
        foreach (var item in data)
        {
            var b64 = item?["b64_json"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(b64))
                continue;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                continue;
            }

            i++;
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{fileName.ToOutputFileName()}-{i}.png",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        return links;
    }

    private static void ValidateImageGenerationRequest(InfomaniakImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.N < 1 || input.N > 5)
            throw new ValidationException("n must be between 1 and 5.");

        ValidateQuality(input.Quality);
        ValidateResponseFormat(input.ResponseFormat);
        ValidateSize(input.Size);
        ValidateStyle(input.Style);
    }

    private static void ValidatePhotoMakerRequest(InfomaniakPhotoMakerRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (input.N < 1 || input.N > 5)
            throw new ValidationException("n must be between 1 and 5.");

        if (input.GuidanceScale.HasValue && input.GuidanceScale.Value <= 0)
            throw new ValidationException("guidance_scale must be greater than 0 when provided.");

        if (input.StyleStrengthRatio.HasValue && (input.StyleStrengthRatio.Value < 10 || input.StyleStrengthRatio.Value > 50))
            throw new ValidationException("style_strength_ratio must be between 10 and 50 when provided.");

        ValidateQuality(input.Quality);
        ValidateResponseFormat(input.ResponseFormat);
        ValidateSize(input.Size);
        ValidateStyle(input.Style);
    }

    private static void ValidateQuality(string quality)
    {
        if (!string.Equals(quality, "standard", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(quality, "hd", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("quality must be one of: standard, hd.");
    }

    private static void ValidateResponseFormat(string responseFormat)
    {
        if (!string.Equals(responseFormat, "b64_json", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("response_format currently only supports: b64_json.");
    }

    private static void ValidateSize(string size)
    {
        if (!string.Equals(size, "1024x1024", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(size, "1024x1792", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(size, "1792x1024", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("size must be one of: 1024x1024, 1024x1792, 1792x1024.");
    }

    private static void ValidateStyle(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cinematic",
            "comic_book",
            "digital_art",
            "disney_charactor",
            "enhance",
            "fantasy_art",
            "line_art",
            "lowpoly",
            "neonpunk",
            "photographic"
        };

        if (!allowed.Contains(style))
            throw new ValidationException("style must be one of: cinematic, comic_book, digital_art, disney_charactor, enhance, fantasy_art, line_art, lowpoly, neonpunk, photographic.");
    }
}

[Description("Please confirm the Infomaniak image generation request.")]
public sealed class InfomaniakImageGenerateRequest
{
    [JsonPropertyName("product_id")]
    [Required]
    [Description("Infomaniak AI product id.")]
    public int ProductId { get; set; }

    [JsonPropertyName("model")]
    [Required]
    [Description("Model name to use.")]
    public string Model { get; set; } = "sdxl_lightning";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text describing the image(s) to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("n")]
    [Required]
    [Range(1, 5)]
    [Description("Number of images to generate (1-5).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("quality")]
    [Required]
    [Description("Image quality: standard or hd.")]
    public string Quality { get; set; } = "standard";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format. Currently only b64_json is supported.")]
    public string ResponseFormat { get; set; } = "b64_json";

    [JsonPropertyName("size")]
    [Required]
    [Description("Image size.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("style")]
    [Description("Optional style.")]
    public string? Style { get; set; }

    [JsonPropertyName("sync")]
    [Required]
    [Description("Sync mode.")]
    public bool Sync { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;

    [JsonPropertyName("confirmation")]
    [Required]
    [Description("Type GENERATE to confirm execution.")]
    public string Confirmation { get; set; } = "GENERATE";
}

[Description("Please confirm the Infomaniak PhotoMaker request.")]
public sealed class InfomaniakPhotoMakerRequest
{
    [JsonPropertyName("product_id")]
    [Required]
    [Description("Infomaniak AI product id.")]
    public int ProductId { get; set; }

    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Single source image URL used for PhotoMaker identity conditioning.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text. Use trigger word img after class word (e.g., man img).")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("n")]
    [Required]
    [Range(1, 5)]
    [Description("Number of images to generate (1-5).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("guidance_scale")]
    [Description("Optional prompt guidance scale.")]
    public double? GuidanceScale { get; set; }

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("quality")]
    [Required]
    [Description("Image quality: standard or hd.")]
    public string Quality { get; set; } = "standard";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format. Currently only b64_json is supported.")]
    public string ResponseFormat { get; set; } = "b64_json";

    [JsonPropertyName("size")]
    [Required]
    [Description("Image size.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("style")]
    [Description("Optional style.")]
    public string? Style { get; set; }

    [JsonPropertyName("style_strength_ratio")]
    [Range(10, 50)]
    [Description("Style strength ratio (10-50).")]
    public int? StyleStrengthRatio { get; set; }

    [JsonPropertyName("sync")]
    [Required]
    [Description("Sync mode.")]
    public bool Sync { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;

    [JsonPropertyName("confirmation")]
    [Required]
    [Description("Type GENERATE to confirm execution.")]
    public string Confirmation { get; set; } = "GENERATE";
}

