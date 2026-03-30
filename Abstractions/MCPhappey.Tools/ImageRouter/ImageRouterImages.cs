using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ImageRouter;

public static class ImageRouterImages
{
    private const string UnifiedPath = "v1/openai/images/edits";

    [Description("Generate or edit images with ImageRouter, optionally using input image/mask fileUrl(s), upload all generated images to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "ImageRouter Images Generate",
        Name = "imagerouter_images_generate",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> ImageRouter_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("ImageRouter model ID, for example openai/gpt-image-1.")] string model,
        [Description("Optional prompt text. Many models require this.")] string? prompt = null,
        [Description("Optional input image URL(s), comma/newline separated. Supports SharePoint/OneDrive/HTTP.")] string? fileUrl = null,
        [Description("Optional mask image URL(s), comma/newline separated. Supports SharePoint/OneDrive/HTTP.")] string? maskUrl = null,
        [Description("Quality: auto, low, medium, high. Default: auto.")] string quality = "auto",
        [Description("Size: auto or WIDTHxHEIGHT (for example 1024x1024). Default: auto.")] string size = "auto",
        [Description("Response format: url, b64_json, b64_ephemeral. Default: url.")] string response_format = "url",
        [Description("Output image format: webp, jpeg, png. Default: webp.")] string output_format = "webp",
        [Description("Output filename base (without extension).")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new ImageRouterImageGenerateRequest
                {
                    Model = model,
                    Prompt = NormalizeOptional(prompt),
                    FileUrl = NormalizeOptional(fileUrl),
                    MaskUrl = NormalizeOptional(maskUrl),
                    Quality = NormalizeRequired(quality, nameof(quality)),
                    Size = NormalizeRequired(size, nameof(size)),
                    ResponseFormat = NormalizeRequired(response_format, nameof(response_format)).ToLowerInvariant(),
                    OutputFormat = NormalizeRequired(output_format, nameof(output_format)).ToLowerInvariant(),
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null)
                return notAccepted;

            if (typed == null)
                return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerateInput(typed);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            using var form = new MultipartFormDataContent();
            AddFormValue(form, "model", typed.Model);
            AddFormValue(form, "quality", typed.Quality);
            AddFormValue(form, "size", typed.Size);
            AddFormValue(form, "response_format", typed.ResponseFormat);
            AddFormValue(form, "output_format", typed.OutputFormat);
            AddFormValue(form, "prompt", typed.Prompt);

            await AddInputFilesAsync(serviceProvider, requestContext, downloadService, form, "image[]", typed.FileUrl, cancellationToken);
            await AddInputFilesAsync(serviceProvider, requestContext, downloadService, form, "mask[]", typed.MaskUrl, cancellationToken);

            using var client = serviceProvider.CreateImageRouterClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, UnifiedPath)
            {
                Content = form
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"ImageRouter image generation failed ({(int)resp.StatusCode}): {raw}");

            var doc = JsonDocument.Parse(raw);
            var links = new List<ResourceLinkBlock>();
            var index = 0;

            foreach (var item in ExtractDataItems(doc.RootElement))
            {
                index++;
                var uploadName = BuildOutputName(typed.Filename, typed.OutputFormat, index);

                if (typed.ResponseFormat == "url")
                {
                    var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                    var file = files.FirstOrDefault();
                    if (file == null)
                        continue;

                    var uploadedByUrl = await requestContext.Server.Upload(
                        serviceProvider,
                        uploadName,
                        BinaryData.FromBytes(file.Contents.ToArray()),
                        cancellationToken);

                    if (uploadedByUrl != null)
                        links.Add(uploadedByUrl);

                    continue;
                }

                var b64 = GetBase64(item);
                if (string.IsNullOrWhiteSpace(b64))
                    continue;

                var bytes = DecodeBase64Payload(b64);
                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    uploadName,
                    BinaryData.FromBytes(bytes),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }

            if (links.Count == 0)
                throw new InvalidOperationException("ImageRouter image generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task AddInputFilesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        DownloadService downloadService,
        MultipartFormDataContent form,
        string fieldName,
        string? urlsRaw,
        CancellationToken cancellationToken)
    {
        foreach (var url in ParseCsvOrLines(urlsRaw))
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            var fileBytes = file.Contents.ToArray();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
            form.Add(fileContent, fieldName, string.IsNullOrWhiteSpace(file.Filename) ? "input.bin" : file.Filename);
        }
    }

    private static IEnumerable<JsonElement> ExtractDataItems(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
                yield return item;
        }
    }

    private static string? GetBase64(JsonElement dataItem)
    {
        if (dataItem.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
            return b64.GetString();

        if (dataItem.TryGetProperty("b64_ephemeral", out var eph) && eph.ValueKind == JsonValueKind.String)
            return eph.GetString();

        return null;
    }

    private static void AddFormValue(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            form.Add(new StringContent(value, Encoding.UTF8), name);
    }

    private static IReadOnlyList<string> ParseCsvOrLines(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static void ValidateGenerateInput(ImageRouterImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        var hasPrompt = !string.IsNullOrWhiteSpace(input.Prompt);
        var hasInputImages = ParseCsvOrLines(input.FileUrl).Count > 0;
        if (!hasPrompt && !hasInputImages)
            throw new ValidationException("Provide prompt and/or fileUrl (input image URL).");

        if (input.ResponseFormat is not ("url" or "b64_json" or "b64_ephemeral"))
            throw new ValidationException("response_format must be one of: url, b64_json, b64_ephemeral.");

        if (input.OutputFormat is not ("webp" or "jpeg" or "png"))
            throw new ValidationException("output_format must be one of: webp, jpeg, png.");
    }

    private static byte[] DecodeBase64Payload(string payload)
    {
        var raw = payload.Trim();
        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            raw = raw[(comma + 1)..];

        return Convert.FromBase64String(raw);
    }

    private static string BuildOutputName(string filenameBase, string outputFormat, int index)
    {
        var ext = outputFormat.ToLowerInvariant() switch
        {
            "jpeg" => ".jpg",
            "png" => ".png",
            _ => ".webp"
        };

        return index == 1
            ? $"{filenameBase}{ext}"
            : $"{filenameBase}-{index}{ext}";
    }

    private static string NormalizeRequired(string value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException($"{field} is required.");

        return trimmed;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    [Description("Please confirm the ImageRouter image generation request details.")]
    public sealed class ImageRouterImageGenerateRequest
    {
        [JsonPropertyName("model")]
        [Required]
        [Description("ImageRouter model ID, for example openai/gpt-image-1.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("prompt")]
        [Description("Optional prompt text.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("fileUrl")]
        [Description("Optional input image URL(s), comma/newline separated.")]
        public string? FileUrl { get; set; }

        [JsonPropertyName("maskUrl")]
        [Description("Optional mask image URL(s), comma/newline separated.")]
        public string? MaskUrl { get; set; }

        [JsonPropertyName("quality")]
        [Required]
        [Description("Quality: auto, low, medium, high.")]
        public string Quality { get; set; } = "auto";

        [JsonPropertyName("size")]
        [Required]
        [Description("Size: auto or WIDTHxHEIGHT.")]
        public string Size { get; set; } = "auto";

        [JsonPropertyName("response_format")]
        [Required]
        [Description("Response format: url, b64_json, b64_ephemeral.")]
        public string ResponseFormat { get; set; } = "url";

        [JsonPropertyName("output_format")]
        [Required]
        [Description("Output image format: webp, jpeg, png.")]
        public string OutputFormat { get; set; } = "webp";

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename base (without extension).")]
        public string Filename { get; set; } = default!;
    }
}

