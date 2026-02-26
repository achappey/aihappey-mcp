using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AICC;

public static class AICCImages
{
    [Description("Generate image(s) via AI.CC using OpenAI, Gemini, Volcengine, or Qwen routes based on requested model, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "AICC Images Generate", Name = "aicc_images_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AICC_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt text for generation.")] string prompt,
        [Description("Model name. Route is inferred from model (OpenAI/Gemini/Volcengine/Qwen).")]
        string model = "gpt-image-1",
        [Description("Number of images to generate (1-10).")][Range(1, 10)] int n = 1,
        [Description("Image size (provider-specific). Example: 1024x1024 or auto.")] string size = "1024x1024",
        [Description("Quality (provider-specific). Example: auto, high, medium, low, hd, standard.")]
        string quality = "auto",
        [Description("Response format. Example: b64_json or url.")] string response_format = "b64_json",
        [Description("Output format. Example: png, jpeg, webp.")] string output_format = "png",
        [Description("Gemini aspect ratio. Example: 1:1, 16:9.")] string aspectRatio = "1:1",
        [Description("Gemini image size label. Example: 1K, 2K, 4K.")] string geminiImageSize = "1K",
        [Description("Optional negative prompt for models that support it.")] string? negative_prompt = null,
        [Description("Optional seed for providers that support it.")] int? seed = null,
        [Description("Enable watermark when provider supports it.")] bool watermark = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new AICCImageGenerateRequest
                {
                    Prompt = prompt,
                    Model = model,
                    N = n,
                    Size = size,
                    Quality = quality,
                    ResponseFormat = response_format,
                    OutputFormat = output_format,
                    AspectRatio = aspectRatio,
                    GeminiImageSize = geminiImageSize,
                    NegativePrompt = negative_prompt,
                    Seed = seed,
                    Watermark = watermark,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerateRequest(typed);
            var provider = InferProvider(typed.Model);

            var client = serviceProvider.GetRequiredService<AICCClient>();
            JsonNode? response = provider switch
            {
                AICCImageProvider.OpenAI => await client.PostJsonAsync("/v1/images/generations", BuildOpenAiGenerateBody(typed), cancellationToken),
                AICCImageProvider.Volcengine => await client.PostJsonAsync("/v1/images/generations", BuildVolcGenerateBody(typed), cancellationToken),
                AICCImageProvider.Qwen => await client.PostJsonAsync("/v1/images/generations", BuildQwenGenerateBody(typed), cancellationToken),
                AICCImageProvider.Gemini => await client.PostJsonAsync($"/v1beta/models/{Uri.EscapeDataString(typed.Model)}:generateContent", BuildGeminiGenerateBody(typed), cancellationToken),
                _ => throw new ValidationException("Unsupported provider route.")
            };

            var links = await UploadOutputsAsync(
                serviceProvider,
                requestContext,
                response,
                typed.Filename,
                GetPreferredOutputExtension(typed.OutputFormat),
                cancellationToken);

            if (links.Count == 0)
                throw new Exception("AICC generation completed but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Edit image(s) via AI.CC route inferred by model, always confirm via elicitation, accepts exactly one input fileUrl, uploads outputs to SharePoint/OneDrive, and returns only resource link blocks.")]
    [McpServerTool(Title = "AICC Images Edit", Name = "aicc_images_edit", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> AICC_Images_Edit(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Single input image URL (SharePoint/OneDrive/public HTTP).")]
        string fileUrl,
        [Description("Prompt text for editing.")] string prompt,
        [Description("Model name. Route is inferred from model.")] string model = "gpt-image-1",
        [Description("Number of images to generate (1-10).")][Range(1, 10)] int n = 1,
        [Description("Image size (provider-specific). Example: 1024x1024 or auto.")] string size = "1024x1024",
        [Description("Quality (provider-specific). Example: auto, high, medium, low, hd, standard.")]
        string quality = "auto",
        [Description("Response format. Example: b64_json or url.")] string response_format = "b64_json",
        [Description("Output format. Example: png, jpeg, webp.")] string output_format = "png",
        [Description("Optional negative prompt for models that support it.")] string? negative_prompt = null,
        [Description("Enable watermark when provider supports it.")] bool watermark = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new AICCImageEditRequest
                {
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    Model = model,
                    N = n,
                    Size = size,
                    Quality = quality,
                    ResponseFormat = response_format,
                    OutputFormat = output_format,
                    NegativePrompt = negative_prompt,
                    Watermark = watermark,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateEditRequest(typed);
            var provider = InferProvider(typed.Model);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var sourceFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var sourceFile = sourceFiles.FirstOrDefault() ?? throw new InvalidOperationException("Could not download source image from fileUrl.");

            var client = serviceProvider.GetRequiredService<AICCClient>();
            JsonNode? response = provider switch
            {
                AICCImageProvider.OpenAI => await PostOpenAiEditAsync(client, typed, sourceFile, cancellationToken),
                AICCImageProvider.Qwen => await client.PostJsonAsync("/v1/images/edits", BuildQwenEditBody(typed,
                    await UploadSourceAndGetUrlAsync(serviceProvider, requestContext, sourceFile, cancellationToken)), cancellationToken),
                AICCImageProvider.Gemini => throw new ValidationException("Gemini image edit route is not available in current AI.CC docs."),
                AICCImageProvider.Volcengine => throw new ValidationException("Volcengine image edit route is not available in current AI.CC docs."),
                _ => throw new ValidationException("Unsupported provider route for edit.")
            };

            var links = await UploadOutputsAsync(
                serviceProvider,
                requestContext,
                response,
                typed.Filename,
                GetPreferredOutputExtension(typed.OutputFormat),
                cancellationToken);

            if (links.Count == 0)
                throw new Exception("AICC image edit completed but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<JsonNode?> PostOpenAiEditAsync(AICCClient client, AICCImageEditRequest input,
        FileItem source,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(input.Model), "model");
        form.Add(new StringContent(input.Prompt), "prompt");
        form.Add(new StringContent(input.N.ToString()), "n");
        form.Add(new StringContent(input.Size), "size");
        form.Add(new StringContent(input.Quality), "quality");
        form.Add(new StringContent(input.ResponseFormat), "response_format");
        form.Add(new StringContent(input.OutputFormat), "output_format");

        var imageContent = new ByteArrayContent(source.Contents.ToArray());
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(source.MimeType ?? "image/png");
        form.Add(imageContent, "image", source.Filename ?? "aicc-edit-source.png");

        return await client.PostMultipartAsync("/v1/images/edits", form, cancellationToken);
    }

    private static object BuildOpenAiGenerateBody(AICCImageGenerateRequest input)
        => new
        {
            model = input.Model,
            prompt = input.Prompt,
            n = input.N,
            size = input.Size,
            quality = input.Quality,
            response_format = input.ResponseFormat,
            output_format = input.OutputFormat
        };

    private static object BuildVolcGenerateBody(AICCImageGenerateRequest input)
        => new
        {
            model = input.Model,
            prompt = input.Prompt,
            size = input.Size,
            seed = input.Seed,
            response_format = input.ResponseFormat,
            watermark = input.Watermark
        };

    private static object BuildQwenGenerateBody(AICCImageGenerateRequest input)
        => new
        {
            model = input.Model,
            input = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { text = input.Prompt }
                        }
                    }
                }
            },
            parameters = new
            {
                n = input.N,
                negative_prompt = input.NegativePrompt,
                watermark = input.Watermark,
                size = input.Size
            }
        };

    private static object BuildQwenEditBody(AICCImageEditRequest input, string imageUrl)
        => new
        {
            model = input.Model,
            input = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { image = imageUrl },
                            new { text = input.Prompt }
                        }
                    }
                }
            },
            parameters = new
            {
                n = input.N,
                negative_prompt = input.NegativePrompt,
                watermark = input.Watermark,
                size = input.Size
            }
        };

    private static object BuildGeminiGenerateBody(AICCImageGenerateRequest input)
        => new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = input.Prompt } }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "TEXT", "IMAGE" },
                imageConfig = new
                {
                    aspectRatio = input.AspectRatio,
                    imageSize = input.GeminiImageSize
                }
            }
        };

    private static async Task<string> UploadSourceAndGetUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        FileItem sourceFile,
        CancellationToken cancellationToken)
    {
        var filename = string.IsNullOrWhiteSpace(sourceFile.Filename)
            ? "aicc-source.png"
            : sourceFile.Filename;

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            filename,
            BinaryData.FromBytes(sourceFile.Contents.ToArray()),
            cancellationToken);

        if (uploaded == null || string.IsNullOrWhiteSpace(uploaded.Uri))
            throw new Exception("Failed to upload source image for provider URL-based edit.");

        return uploaded.Uri;
    }

    private static async Task<List<ResourceLinkBlock>> UploadOutputsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        JsonNode? response,
        string filename,
        string defaultExtension,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();
        if (response == null)
            return links;

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var binaries = new List<byte[]>();

        CollectOutputPayload(response, urls, binaries);

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var outputName = filename.ToOutputFileName();
        var index = 0;

        foreach (var bytes in binaries)
        {
            if (bytes.Length == 0)
                continue;

            index++;
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{outputName}-{index}{defaultExtension}",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        foreach (var url in urls)
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            index++;
            var ext = GetImageExtension(file.Filename, file.MimeType, defaultExtension);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{outputName}-{index}{ext}",
                BinaryData.FromBytes(file.Contents.ToArray()),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        return links;
    }

    private static void CollectOutputPayload(JsonNode node, HashSet<string> urls, List<byte[]> binaries)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    var key = kv.Key;
                    var value = kv.Value;
                    if (value == null)
                        continue;

                    if (value is JsonValue jv)
                    {
                        var s = jv.TryGetValue<string>(out var str) ? str : null;
                        if (string.IsNullOrWhiteSpace(s))
                            continue;

                        if (IsUrlKey(key) && Uri.TryCreate(s, UriKind.Absolute, out _))
                            urls.Add(s);

                        if (IsBase64Key(key) && TryDecodeBase64(s, out var bytes))
                            binaries.Add(bytes);
                    }

                    CollectOutputPayload(value, urls, binaries);
                }

                break;

            case JsonArray arr:
                foreach (var child in arr)
                {
                    if (child != null)
                        CollectOutputPayload(child, urls, binaries);
                }

                break;
        }
    }

    private static bool IsUrlKey(string key)
        => key.Equals("url", StringComparison.OrdinalIgnoreCase)
           || key.Equals("fileUri", StringComparison.OrdinalIgnoreCase)
           || key.Equals("file_uri", StringComparison.OrdinalIgnoreCase);

    private static bool IsBase64Key(string key)
        => key.Equals("b64_json", StringComparison.OrdinalIgnoreCase)
           || key.Equals("data", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static AICCImageProvider InferProvider(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return AICCImageProvider.OpenAI;

        if (model.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
            return AICCImageProvider.Gemini;

        if (model.StartsWith("doubao", StringComparison.OrdinalIgnoreCase)
            || model.Contains("seedream", StringComparison.OrdinalIgnoreCase))
            return AICCImageProvider.Volcengine;

        if (model.StartsWith("qwen", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("wan", StringComparison.OrdinalIgnoreCase))
            return AICCImageProvider.Qwen;

        return AICCImageProvider.OpenAI;
    }

    private static void ValidateGenerateRequest(AICCImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.N < 1 || input.N > 10)
            throw new ValidationException("n must be between 1 and 10.");
    }

    private static void ValidateEditRequest(AICCImageEditRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.N < 1 || input.N > 10)
            throw new ValidationException("n must be between 1 and 10.");
    }

    private static string GetPreferredOutputExtension(string outputFormat)
        => outputFormat.Trim().ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => ".jpg",
            "webp" => ".webp",
            _ => ".png"
        };

    private static string GetImageExtension(string? filename, string? mimeType, string fallback)
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
            _ => fallback
        };
    }

    private enum AICCImageProvider
    {
        OpenAI,
        Gemini,
        Volcengine,
        Qwen
    }
}

[Description("Please confirm the AICC image generation request.")]
public sealed class AICCImageGenerateRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text for generation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model name used for route selection and request execution.")]
    public string Model { get; set; } = "gpt-image-1";

    [JsonPropertyName("n")]
    [Range(1, 10)]
    [Description("Number of images to generate (1-10).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("size")]
    [Required]
    [Description("Image size (provider-specific).")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("quality")]
    [Required]
    [Description("Quality (provider-specific).")]
    public string Quality { get; set; } = "auto";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format, for example b64_json or url.")]
    public string ResponseFormat { get; set; } = "b64_json";

    [JsonPropertyName("output_format")]
    [Required]
    [Description("Output format, for example png, jpeg, webp.")]
    public string OutputFormat { get; set; } = "png";

    [JsonPropertyName("aspectRatio")]
    [Description("Gemini aspect ratio.")]
    public string AspectRatio { get; set; } = "1:1";

    [JsonPropertyName("geminiImageSize")]
    [Description("Gemini image size label.")]
    public string GeminiImageSize { get; set; } = "1K";

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("seed")]
    [Description("Optional seed.")]
    public int? Seed { get; set; }

    [JsonPropertyName("watermark")]
    [Description("Enable watermark when provider supports it.")]
    public bool Watermark { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the AICC image edit request.")]
public sealed class AICCImageEditRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Single input image URL (SharePoint/OneDrive/public HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt text for editing.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Model name used for route selection and request execution.")]
    public string Model { get; set; } = "gpt-image-1";

    [JsonPropertyName("n")]
    [Range(1, 10)]
    [Description("Number of images to generate (1-10).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("size")]
    [Required]
    [Description("Image size (provider-specific).")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("quality")]
    [Required]
    [Description("Quality (provider-specific).")]
    public string Quality { get; set; } = "auto";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format, for example b64_json or url.")]
    public string ResponseFormat { get; set; } = "b64_json";

    [JsonPropertyName("output_format")]
    [Required]
    [Description("Output format, for example png, jpeg, webp.")]
    public string OutputFormat { get; set; } = "png";

    [JsonPropertyName("negative_prompt")]
    [Description("Optional negative prompt.")]
    public string? NegativePrompt { get; set; }

    [JsonPropertyName("watermark")]
    [Description("Enable watermark when provider supports it.")]
    public bool Watermark { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

