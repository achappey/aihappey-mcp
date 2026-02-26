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

namespace MCPhappey.Tools.Monica.Images;

public static class MonicaImages
{
    private const string ApiBaseUrl = "https://openapi.monica.im";

    [Description("Generate image(s) with Monica using a common provider-based tool, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Monica Image Generation", Name = "monica_images_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Monica_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt for image generation.")] string prompt,
        [Description("Generation provider family: flux, sd, dalle, playground, ideogram.")] string provider = "flux",
        [Description("Provider model identifier. If omitted, a provider-specific default is used.")] string? model = null,
        [Description("Number of images to generate where supported.")][Range(1, 4)] int count = 1,
        [Description("Output size. Values depend on provider.")] string size = "1024x1024",
        [Description("Optional negative prompt where supported.")] string? negativePrompt = null,
        [Description("Optional random seed.")] int? seed = null,
        [Description("Optional denoising/sampling steps where supported.")] int? steps = null,
        [Description("Optional guidance/cfg scale where supported.")] double? guidance = null,
        [Description("Optional style value. For DALL·E: vivid|natural. For Ideogram: AUTO|GENERAL|REALISTIC|DESIGN|RENDER_3D|ANIME.")] string? style = null,
        [Description("Optional quality value for DALL·E: standard|hd.")] string? quality = null,
        [Description("Optional Ideogram aspect ratio (ASPECT_1_1, ASPECT_16_9, etc.).")] string? aspectRatio = null,
        [Description("Optional Ideogram magic prompt option: AUTO|ON|OFF.")] string? magicPromptOption = null,
        [Description("Optional Flux Pro interval (1-4).")][Range(1, 4)] int? interval = null,
        [Description("Optional Flux Pro safety tolerance (1-6).")][Range(1, 6)] int? safetyTolerance = null,
        [Description("Optional output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new MonicaGenerateImageRequest
                {
                    Prompt = prompt,
                    Provider = provider,
                    Model = model,
                    Count = count,
                    Size = size,
                    NegativePrompt = negativePrompt,
                    Seed = seed,
                    Steps = steps,
                    Guidance = guidance,
                    Style = style,
                    Quality = quality,
                    AspectRatio = aspectRatio,
                    MagicPromptOption = magicPromptOption,
                    Interval = interval,
                    SafetyTolerance = safetyTolerance,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            ValidateGenerateRequest(typed);

            var path = ResolveGeneratePathAndNormalize(typed);
            var body = BuildGeneratePayload(typed);
            var response = await PostMonicaAsync(serviceProvider, path, body, cancellationToken);

            var resultUrls = ExtractImageUrls(response);
            if (resultUrls.Count == 0)
                throw new Exception("Monica generation succeeded but no image URL(s) were returned.");

            var links = await DownloadUploadFromUrlsAsync(
                serviceProvider,
                requestContext,
                resultUrls,
                typed.Filename,
                cancellationToken);

            if (links.Count == 0)
                throw new Exception("Monica generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Upscale an image with Monica from a single fileUrl, upload the result to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Monica Upscale", Name = "monica_images_upscale", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Monica_Images_Upscale(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input file URL. SharePoint/OneDrive secure links are supported.")] string fileUrl,
        [Description("Upscale factor. Monica currently supports only 2.")][Range(2, 2)] int scale = 2,
        [Description("Optional output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new MonicaUpscaleRequest
                {
                    FileUrl = fileUrl,
                    Scale = scale,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);


            if (string.IsNullOrWhiteSpace(typed.FileUrl))
                throw new ValidationException("fileUrl is required.");

            if (typed.Scale != 2)
                throw new ValidationException("scale must be 2.");

            var sourceImageUrl = await GetMonicaAccessibleUrlFromFileUrlAsync(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var body = new JsonObject
            {
                ["image"] = sourceImageUrl,
                ["scale"] = typed.Scale
            };

            var response = await PostMonicaAsync(serviceProvider, "/v1/image/tool/upscale", body, cancellationToken);
            var urls = ExtractImageUrls(response);
            if (urls.Count == 0)
                throw new Exception("Monica upscale succeeded but no image URL was returned.");

            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, urls, typed.Filename, cancellationToken);
            if (links.Count == 0)
                throw new Exception("Monica upscale succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Remove image background with Monica from a single fileUrl, upload the result to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Monica remove background", Name = "monica_images_remove_background", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Monica_Images_RemoveBackground(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Input file URL. SharePoint/OneDrive secure links are supported.")] string fileUrl,
        [Description("Optional output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new MonicaRemoveBackgroundRequest
                {
                    FileUrl = fileUrl,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(typed.FileUrl))
                throw new ValidationException("fileUrl is required.");

            var sourceImageUrl = await GetMonicaAccessibleUrlFromFileUrlAsync(serviceProvider, requestContext, typed.FileUrl, cancellationToken);
            var body = new JsonObject
            {
                ["image"] = sourceImageUrl
            };

            var response = await PostMonicaAsync(serviceProvider, "/v1/image/tool/removebg", body, cancellationToken);
            var urls = ExtractImageUrls(response);
            if (urls.Count == 0)
                throw new Exception("Monica remove-background succeeded but no image URL was returned.");

            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, urls, typed.Filename, cancellationToken);
            if (links.Count == 0)
                throw new Exception("Monica remove-background succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<JsonNode> PostMonicaAsync(
        IServiceProvider serviceProvider,
        string path,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<MonicaSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {raw}");

        return JsonNode.Parse(raw) ?? new JsonObject();
    }

    private static async Task<string> GetMonicaAccessibleUrlFromFileUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download file content from fileUrl.");

        var sourceFilename = string.IsNullOrWhiteSpace(file.Filename)
            ? $"monica-source{GetImageExtension(null, file.MimeType)}"
            : file.Filename;

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            sourceFilename,
            BinaryData.FromBytes(file.Contents.ToArray()),
            cancellationToken);

        if (uploaded == null || string.IsNullOrWhiteSpace(uploaded.Uri))
            throw new Exception("Failed to upload source file for Monica processing.");

        return uploaded.Uri;
    }

    private static async Task<List<ResourceLinkBlock>> DownloadUploadFromUrlsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        IEnumerable<string> urls,
        string filename,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var links = new List<ResourceLinkBlock>();
        var i = 0;

        foreach (var url in urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            i++;
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            var ext = GetImageExtension(file.Filename, file.MimeType);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{filename.ToOutputFileName()}-{i}{ext}",
                BinaryData.FromBytes(file.Contents.ToArray()),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        return links;
    }

    private static List<string> ExtractImageUrls(JsonNode response)
    {
        var urls = new List<string>();

        if (response["image"] is JsonValue imageValue)
        {
            var url = imageValue.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
        }

        var data = response["data"] as JsonArray;
        if (data != null)
        {
            foreach (var item in data)
            {
                var url = item?["url"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(url))
                    urls.Add(url);
            }
        }

        return urls;
    }

    private static string ResolveGeneratePathAndNormalize(MonicaGenerateImageRequest input)
    {
        var provider = input.Provider.Trim().ToLowerInvariant();

        input.Provider = provider;
        input.Model = string.IsNullOrWhiteSpace(input.Model)
            ? provider switch
            {
                "flux" => "flux_pro",
                "sd" => "sd3_5",
                "dalle" => "dall-e-3",
                "playground" => "playground-v2-5",
                "ideogram" => "V_2",
                _ => input.Model
            }
            : input.Model;

        return provider switch
        {
            "flux" => "/v1/image/gen/flux",
            "sd" => "/v1/image/gen/sd",
            "dalle" => "/v1/image/gen/dalle",
            "playground" => "/v1/image/gen/playground",
            "ideogram" => "/v1/image/gen/ideogram",
            _ => throw new ValidationException("provider must be one of: flux, sd, dalle, playground, ideogram.")
        };
    }

    private static JsonObject BuildGeneratePayload(MonicaGenerateImageRequest input)
    {
        return input.Provider switch
        {
            "flux" => BuildFluxPayload(input),
            "sd" => BuildStableDiffusionPayload(input),
            "dalle" => BuildDallePayload(input),
            "playground" => BuildPlaygroundPayload(input),
            "ideogram" => BuildIdeogramPayload(input),
            _ => throw new ValidationException("provider must be one of: flux, sd, dalle, playground, ideogram.")
        };
    }

    private static JsonObject BuildFluxPayload(MonicaGenerateImageRequest input)
    {
        var payload = new JsonObject
        {
            ["model"] = input.Model,
            ["prompt"] = input.Prompt,
            ["num_outputs"] = input.Count,
            ["size"] = input.Size
        };

        if (input.Seed.HasValue) payload["seed"] = input.Seed.Value;
        if (input.Steps.HasValue) payload["steps"] = input.Steps.Value;
        if (input.Guidance.HasValue) payload["guidance"] = input.Guidance.Value;
        if (input.Interval.HasValue) payload["interval"] = input.Interval.Value;
        if (input.SafetyTolerance.HasValue) payload["safety_tolerance"] = input.SafetyTolerance.Value;

        return payload;
    }

    private static JsonObject BuildStableDiffusionPayload(MonicaGenerateImageRequest input)
    {
        var payload = new JsonObject
        {
            ["model"] = input.Model,
            ["prompt"] = input.Prompt,
            ["size"] = input.Size
        };

        if (!string.IsNullOrWhiteSpace(input.NegativePrompt)) payload["negative_prompt"] = input.NegativePrompt;
        if (input.Seed.HasValue) payload["seed"] = input.Seed.Value;
        if (input.Steps.HasValue) payload["steps"] = input.Steps.Value;
        if (input.Guidance.HasValue) payload["cfg_scale"] = input.Guidance.Value;

        // SD docs specify num_outputs only for sdxl. Keep common primitive and let API validate model-specific support.
        payload["num_outputs"] = input.Count;

        return payload;
    }

    private static JsonObject BuildDallePayload(MonicaGenerateImageRequest input)
    {
        var payload = new JsonObject
        {
            ["prompt"] = input.Prompt,
            ["model"] = input.Model,
            ["n"] = input.Count,
            ["size"] = input.Size
        };

        if (!string.IsNullOrWhiteSpace(input.Quality)) payload["quality"] = input.Quality;
        if (!string.IsNullOrWhiteSpace(input.Style)) payload["style"] = input.Style;

        return payload;
    }

    private static JsonObject BuildPlaygroundPayload(MonicaGenerateImageRequest input)
    {
        var payload = new JsonObject
        {
            ["model"] = input.Model,
            ["prompt"] = input.Prompt,
            ["count"] = input.Count,
            ["size"] = input.Size
        };

        if (!string.IsNullOrWhiteSpace(input.NegativePrompt)) payload["negative_prompt"] = input.NegativePrompt;
        if (input.Seed.HasValue) payload["seed"] = input.Seed.Value;
        if (input.Steps.HasValue) payload["step"] = input.Steps.Value;
        if (input.Guidance.HasValue) payload["cfg_scale"] = input.Guidance.Value;

        return payload;
    }

    private static JsonObject BuildIdeogramPayload(MonicaGenerateImageRequest input)
    {
        var payload = new JsonObject
        {
            ["prompt"] = input.Prompt,
            ["model"] = input.Model
        };

        if (!string.IsNullOrWhiteSpace(input.AspectRatio)) payload["aspect_ratio"] = input.AspectRatio;
        if (!string.IsNullOrWhiteSpace(input.MagicPromptOption)) payload["magic_prompt_option"] = input.MagicPromptOption;
        if (input.Seed.HasValue) payload["seed"] = input.Seed.Value;
        if (!string.IsNullOrWhiteSpace(input.NegativePrompt)) payload["negative_prompt"] = input.NegativePrompt;
        if (!string.IsNullOrWhiteSpace(input.Style)) payload["style_type"] = input.Style;

        return payload;
    }

    private static void ValidateGenerateRequest(MonicaGenerateImageRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Provider))
            throw new ValidationException("provider is required.");

        if (input.Count < 1 || input.Count > 4)
            throw new ValidationException("count must be between 1 and 4.");

        var provider = input.Provider.Trim().ToLowerInvariant();
        ValidateProvider(provider);

        if (!string.IsNullOrWhiteSpace(input.AspectRatio) && provider != "ideogram")
            throw new ValidationException("aspectRatio is only supported for provider=ideogram.");

        if (!string.IsNullOrWhiteSpace(input.MagicPromptOption) && provider != "ideogram")
            throw new ValidationException("magicPromptOption is only supported for provider=ideogram.");

        if (input.Interval.HasValue && provider != "flux")
            throw new ValidationException("interval is only supported for provider=flux.");

        if (input.SafetyTolerance.HasValue && provider != "flux")
            throw new ValidationException("safetyTolerance is only supported for provider=flux.");

        if (!string.IsNullOrWhiteSpace(input.Quality) && provider != "dalle")
            throw new ValidationException("quality is only supported for provider=dalle.");

        ValidateSizeForProvider(provider, input.Size);
        ValidateProviderSpecificValues(provider, input);
    }

    private static void ValidateProvider(string provider)
    {
        if (provider != "flux" && provider != "sd" && provider != "dalle" && provider != "playground" && provider != "ideogram")
            throw new ValidationException("provider must be one of: flux, sd, dalle, playground, ideogram.");
    }

    private static void ValidateSizeForProvider(string provider, string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            throw new ValidationException("size is required.");

        var value = size.Trim();
        var fluxSdPlaygroundSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1024x1024", "768x1344", "1344x768"
        };

        var dalleSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1024x1024", "1024x1792", "1792x1024"
        };

        if ((provider == "flux" || provider == "sd" || provider == "playground") && !fluxSdPlaygroundSizes.Contains(value))
            throw new ValidationException("size must be one of: 1024x1024, 768x1344, 1344x768 for provider flux|sd|playground.");

        if (provider == "dalle" && !dalleSizes.Contains(value))
            throw new ValidationException("size must be one of: 1024x1024, 1024x1792, 1792x1024 for provider dalle.");
    }

    private static void ValidateProviderSpecificValues(string provider, MonicaGenerateImageRequest input)
    {
        if (provider == "dalle" && input.Count != 1)
            throw new ValidationException("count must be 1 for provider=dalle.");

        if (!string.IsNullOrWhiteSpace(input.Quality)
            && !string.Equals(input.Quality, "standard", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.Quality, "hd", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("quality must be one of: standard, hd.");

        if (provider == "dalle" && !string.IsNullOrWhiteSpace(input.Style)
            && !string.Equals(input.Style, "vivid", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.Style, "natural", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("style must be one of: vivid, natural for provider=dalle.");

        if (provider == "ideogram" && !string.IsNullOrWhiteSpace(input.Style))
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AUTO", "GENERAL", "REALISTIC", "DESIGN", "RENDER_3D", "ANIME"
            };

            if (!allowed.Contains(input.Style))
                throw new ValidationException("style must be one of: AUTO, GENERAL, REALISTIC, DESIGN, RENDER_3D, ANIME for provider=ideogram.");
        }

        if (provider == "ideogram" && !string.IsNullOrWhiteSpace(input.MagicPromptOption)
            && !string.Equals(input.MagicPromptOption, "AUTO", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.MagicPromptOption, "ON", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(input.MagicPromptOption, "OFF", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("magicPromptOption must be one of: AUTO, ON, OFF for provider=ideogram.");

        if (provider == "ideogram" && !string.IsNullOrWhiteSpace(input.AspectRatio))
        {
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ASPECT_10_16", "ASPECT_16_10", "ASPECT_9_16", "ASPECT_16_9", "ASPECT_3_2",
                "ASPECT_2_3", "ASPECT_4_3", "ASPECT_3_4", "ASPECT_1_1", "ASPECT_1_3", "ASPECT_3_1"
            };

            if (!allowed.Contains(input.AspectRatio))
                throw new ValidationException("aspectRatio is invalid for provider=ideogram.");
        }
    }

    private static string GetImageExtension(string? filename, string? mimeType)
    {
        var ext = Path.GetExtension(filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".png"
        };
    }
}

[Description("Please confirm the Monica image generation request details.")]
public sealed class MonicaGenerateImageRequest
{
    [JsonPropertyName("provider")]
    [Required]
    [Description("Provider family: flux, sd, dalle, playground, ideogram.")]
    public string Provider { get; set; } = "flux";

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt for image generation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("model")]
    [Description("Model identifier. If empty, provider default is used.")]
    public string? Model { get; set; }

    [JsonPropertyName("count")]
    [Range(1, 4)]
    [Description("Number of images where supported.")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("size")]
    [Required]
    [Description("Image size. Provider-specific constraints apply.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("negativePrompt")]
    [Description("Optional negative prompt where supported.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("seed")]
    [Description("Optional random seed.")]
    public int? Seed { get; set; }

    [JsonPropertyName("steps")]
    [Description("Optional denoising/sampling steps where supported.")]
    public int? Steps { get; set; }

    [JsonPropertyName("guidance")]
    [Description("Optional guidance/cfg scale where supported.")]
    public double? Guidance { get; set; }

    [JsonPropertyName("style")]
    [Description("Optional style. DALL·E style or Ideogram style_type based on provider.")]
    public string? Style { get; set; }

    [JsonPropertyName("quality")]
    [Description("Optional quality for DALL·E: standard|hd.")]
    public string? Quality { get; set; }

    [JsonPropertyName("aspectRatio")]
    [Description("Optional Ideogram aspect ratio.")]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("magicPromptOption")]
    [Description("Optional Ideogram magic prompt option: AUTO|ON|OFF.")]
    public string? MagicPromptOption { get; set; }

    [JsonPropertyName("interval")]
    [Range(1, 4)]
    [Description("Optional Flux Pro interval (1-4).")]
    public int? Interval { get; set; }

    [JsonPropertyName("safetyTolerance")]
    [Range(1, 6)]
    [Description("Optional Flux Pro safety tolerance (1-6).")]
    public int? SafetyTolerance { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;

}

[Description("Please confirm the Monica image upscale request details.")]
public sealed class MonicaUpscaleRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input file URL. SharePoint/OneDrive secure links are supported.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("scale")]
    [Range(2, 2)]
    [Description("Upscale factor. Currently only 2 is supported.")]
    public int Scale { get; set; } = 2;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;

}

[Description("Please confirm the Monica remove-background request details.")]
public sealed class MonicaRemoveBackgroundRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Input file URL. SharePoint/OneDrive secure links are supported.")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;
}

