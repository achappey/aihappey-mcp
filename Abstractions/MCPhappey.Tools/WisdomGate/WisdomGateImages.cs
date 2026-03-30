using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.WisdomGate;

public static class WisdomGateImages
{
    [Description("Generate an image with Wisdom Gate Gemini image models and return uploaded resource links.")]
    [McpServerTool(
        Title = "Wisdom Gate Text-to-Image",
        Name = "wisdomgate_images_text_to_image",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WisdomGate_Images_TextToImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the image to generate.")] string prompt,
        [Description("Gemini model ID. Example: gemini-2.5-flash-image or gemini-3-pro-image-preview.")] string model = "gemini-2.5-flash-image",
        [Description("Aspect ratio. One of: 1:1, 3:2, 2:3, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9.")] string aspectRatio = "1:1",
        [Description("Optional image size: 1K, 2K, or 4K.")] string? imageSize = null,
        [Description("Include text modality in addition to IMAGE. If false, forces image-only output.")] bool includeText = false,
        [Description("Output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var input = new WisdomGateTextToImageRequest
            {
                Prompt = prompt,
                Model = model,
                AspectRatio = aspectRatio,
                ImageSize = imageSize,
                IncludeText = includeText,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
            };

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateImageRequest(typed.Model, typed.Prompt, typed.AspectRatio, typed.ImageSize);

            using var client = serviceProvider.CreateWisdomGateClient();

            var payload = BuildImagePayload(
                typed.Prompt,
                typed.AspectRatio,
                typed.ImageSize,
                typed.IncludeText,
                inlineData: null);

            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1beta/models/{Uri.EscapeDataString(typed.Model)}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Wisdom Gate image generation failed ({resp.StatusCode}): {json}");

            return await UploadGeneratedImagesAsync(serviceProvider, requestContext, typed.Filename, json, cancellationToken);
        });

    [Description("Generate an image from text and input image fileUrl with Wisdom Gate Gemini models and return uploaded resource links.")]
    [McpServerTool(
        Title = "Wisdom Gate Image-to-Image",
        Name = "wisdomgate_images_image_to_image",
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> WisdomGate_Images_ImageToImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the target image.")] string prompt,
        [Description("Input image file URL. Supports SharePoint and OneDrive secured links.")] string fileUrl,
        [Description("Gemini model ID. Example: gemini-2.5-flash-image or gemini-3-pro-image-preview.")] string model = "gemini-2.5-flash-image",
        [Description("Aspect ratio. One of: 1:1, 3:2, 2:3, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9.")] string aspectRatio = "1:1",
        [Description("Optional image size: 1K, 2K, or 4K.")] string? imageSize = null,
        [Description("Include text modality in addition to IMAGE. If false, forces image-only output.")] bool includeText = false,
        [Description("Output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var input = new WisdomGateImageToImageRequest
            {
                Prompt = prompt,
                FileUrl = fileUrl,
                Model = model,
                AspectRatio = aspectRatio,
                ImageSize = imageSize,
                IncludeText = includeText,
                Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName("png")
            };

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(input, cancellationToken);
            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateImageRequest(typed.Model, typed.Prompt, typed.AspectRatio, typed.ImageSize);

            if (string.IsNullOrWhiteSpace(typed.FileUrl))
                throw new ValidationException("fileUrl is required.");

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var source = files.FirstOrDefault() ?? throw new Exception("Failed to download fileUrl content.");

            var inlineMimeType = string.IsNullOrWhiteSpace(source.MimeType) ? "image/png" : source.MimeType!;
            var inlineData = Convert.ToBase64String(source.Contents.ToArray());

            using var client = serviceProvider.CreateWisdomGateClient();

            var payload = BuildImagePayload(
                typed.Prompt,
                typed.AspectRatio,
                typed.ImageSize,
                typed.IncludeText,
                new InlineDataPart(inlineMimeType, inlineData));

            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"/v1beta/models/{Uri.EscapeDataString(typed.Model)}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Wisdom Gate image edit failed ({resp.StatusCode}): {json}");

            return await UploadGeneratedImagesAsync(serviceProvider, requestContext, typed.Filename, json, cancellationToken);
        });

    private static object BuildImagePayload(
        string prompt,
        string aspectRatio,
        string? imageSize,
        bool includeText,
        InlineDataPart? inlineData)
    {
        var parts = new List<object> { new { text = prompt } };
        if (inlineData != null)
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = inlineData.MimeType,
                    data = inlineData.Data
                }
            });
        }

        var responseModalities = includeText
            ? new[] { "TEXT", "IMAGE" }
            : new[] { "IMAGE" };

        object generationConfig;
        if (string.IsNullOrWhiteSpace(imageSize))
        {
            generationConfig = new
            {
                responseModalities,
                imageConfig = new
                {
                    aspectRatio
                }
            };
        }
        else
        {
            generationConfig = new
            {
                responseModalities,
                imageConfig = new
                {
                    aspectRatio,
                    imageSize
                }
            };
        }

        return new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = parts.ToArray()
                }
            },
            generationConfig
        };
    }

    private static async Task<CallToolResult?> UploadGeneratedImagesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string filenameBase,
        string json,
        CancellationToken cancellationToken)
    {
        var links = new List<ResourceLinkBlock>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            throw new Exception("Wisdom Gate did not return candidates with image data.");

        var index = 0;
        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
                continue;

            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (!TryGetInlineData(part, out var mimeType, out var data))
                    continue;

                var bytes = Convert.FromBase64String(data);
                var ext = GetImageExtension(mimeType);

                index++;
                var name = index == 1
                    ? $"{filenameBase}{ext}"
                    : $"{filenameBase}-{index}{ext}";

                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    name,
                    BinaryData.FromBytes(bytes),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }
        }

        if (links.Count == 0)
            throw new Exception("Wisdom Gate returned no inline image data to upload.");

        return links.ToResourceLinkCallToolResponse();
    }

    private static bool TryGetInlineData(JsonElement part, out string mimeType, out string data)
    {
        mimeType = string.Empty;
        data = string.Empty;

        if (part.TryGetProperty("inlineData", out var inlineData) && inlineData.ValueKind == JsonValueKind.Object)
        {
            mimeType = inlineData.TryGetProperty("mimeType", out var mimeEl) ? mimeEl.GetString() ?? "image/png" : "image/png";
            data = inlineData.TryGetProperty("data", out var dataEl) ? dataEl.GetString() ?? string.Empty : string.Empty;
            return !string.IsNullOrWhiteSpace(data);
        }

        if (part.TryGetProperty("inline_data", out var snakeInlineData) && snakeInlineData.ValueKind == JsonValueKind.Object)
        {
            mimeType = snakeInlineData.TryGetProperty("mime_type", out var mimeEl) ? mimeEl.GetString() ?? "image/png" : "image/png";
            data = snakeInlineData.TryGetProperty("data", out var dataEl) ? dataEl.GetString() ?? string.Empty : string.Empty;
            return !string.IsNullOrWhiteSpace(data);
        }

        return false;
    }

    private static void ValidateImageRequest(string model, string prompt, string aspectRatio, string? imageSize)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        if (string.IsNullOrWhiteSpace(prompt))
            throw new ValidationException("prompt is required.");

        var allowedAspectRatios = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1:1", "3:2", "2:3", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9"
        };

        if (!allowedAspectRatios.Contains(aspectRatio))
            throw new ValidationException("aspectRatio must be one of: 1:1, 3:2, 2:3, 3:4, 4:3, 4:5, 5:4, 9:16, 16:9, 21:9.");

        if (!string.IsNullOrWhiteSpace(imageSize))
        {
            var allowedSizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1K", "2K", "4K" };
            if (!allowedSizes.Contains(imageSize))
                throw new ValidationException("imageSize must be one of: 1K, 2K, 4K.");
        }
    }

    private static string GetImageExtension(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/svg+xml" => ".svg",
            _ => ".png"
        };
    }

    private sealed record InlineDataPart(string MimeType, string Data);

    [Description("Please confirm the Wisdom Gate text-to-image request details.")]
    public class WisdomGateTextToImageRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the image to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Gemini model ID. Example: gemini-2.5-flash-image or gemini-3-pro-image-preview.")]
        public string Model { get; set; } = "gemini-2.5-flash-image";

        [JsonPropertyName("aspectRatio")]
        [Required]
        [Description("Aspect ratio.")]
        public string AspectRatio { get; set; } = "1:1";

        [JsonPropertyName("imageSize")]
        [Description("Optional image size: 1K, 2K, or 4K.")]
        public string? ImageSize { get; set; }

        [JsonPropertyName("includeText")]
        [Description("Include text modality in addition to IMAGE.")]
        public bool IncludeText { get; set; }

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename base.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please confirm the Wisdom Gate image-to-image request details.")]
    public sealed class WisdomGateImageToImageRequest : WisdomGateTextToImageRequest
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Input image file URL. Supports SharePoint and OneDrive secured links.")]
        public string FileUrl { get; set; } = default!;
    }
}

