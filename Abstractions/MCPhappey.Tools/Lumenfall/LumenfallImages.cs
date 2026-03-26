using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Lumenfall;

public static class LumenfallImages
{
    [Description("Generate image(s) with Lumenfall, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Lumenfall Images Generate", Name = "lumenfall_images_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Lumenfall_Images_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text prompt for image generation.")] string prompt,
        [Description("Lumenfall image model.")] string model = "gemini-3-pro-image",
        [Description("Number of images to generate (1-10).")][Range(1, 10)] int n = 1,
        [Description("Output size.")] string size = "1024x1024",
        [Description("Image quality.")] string quality = "standard",
        [Description("Response format: url or b64_json.")] string response_format = "url",
        [Description("Output image format: png, jpeg, gif, webp, or avif.")] string output_format = "png",
        [Description("Output compression for lossy formats (1-100).")][Range(1, 100)] int output_compression = 100,
        [Description("Image style.")] string style = "vivid",
        [Description("Optional end-user identifier.")] string? user = null,
        [Description("If true, only returns cost estimate.")] bool dryRun = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LumenfallImageGenerateRequest
                {
                    Prompt = prompt,
                    Model = model,
                    N = n,
                    Size = size,
                    Quality = quality,
                    ResponseFormat = response_format,
                    OutputFormat = output_format,
                    OutputCompression = output_compression,
                    Style = style,
                    User = user,
                    DryRun = dryRun,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateGenerate(typed);

            var body = new JsonObject
            {
                ["prompt"] = typed.Prompt,
                ["model"] = typed.Model,
                ["n"] = typed.N,
                ["size"] = typed.Size,
                ["quality"] = typed.Quality,
                ["response_format"] = typed.ResponseFormat,
                ["output_format"] = typed.OutputFormat,
                ["output_compression"] = typed.OutputCompression,
                ["style"] = typed.Style
            };

            if (!string.IsNullOrWhiteSpace(typed.User))
                body["user"] = typed.User;

            var client = serviceProvider.GetRequiredService<LumenfallClient>();
            var path = typed.DryRun ? "images/generations?dryRun=true" : "images/generations";
            var response = await client.PostJsonAsync(path, body, cancellationToken)
                ?? throw new Exception("Lumenfall returned no response body.");

            if (typed.DryRun)
                return response.ToJsonString().ToJsonCallToolResponse("https://api.lumenfall.ai/openai/v1/images/generations?dryRun=true");

            var links = await UploadImageOutputsAsync(
                serviceProvider,
                requestContext,
                response,
                typed.Filename,
                typed.OutputFormat,
                cancellationToken);

            if (links.Count == 0)
                throw new Exception("Lumenfall image generation completed but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Edit an image with Lumenfall from a single fileUrl, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Lumenfall Images Edit", Name = "lumenfall_images_edit", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Lumenfall_Images_Edit(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Single source image URL (SharePoint/OneDrive/public HTTP).")]
        string fileUrl,
        [Description("Prompt describing the requested edit.")] string prompt,
        [Description("Lumenfall image model.")] string model = "gemini-3-pro-image",
        [Description("Optional mask image URL.")] string? maskFileUrl = null,
        [Description("Number of images to generate (1-10).")][Range(1, 10)] int n = 1,
        [Description("Output size.")] string size = "1024x1024",
        [Description("Image quality.")] string quality = "auto",
        [Description("Response format: url or b64_json.")] string response_format = "url",
        [Description("Output image format: png, jpeg, gif, webp, or avif.")] string output_format = "png",
        [Description("Output compression for lossy formats (1-100).")][Range(1, 100)] int output_compression = 100,
        [Description("Optional end-user identifier.")] string? user = null,
        [Description("If true, only returns cost estimate.")] bool dryRun = false,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new LumenfallImageEditRequest
                {
                    FileUrl = fileUrl,
                    Prompt = prompt,
                    Model = model,
                    MaskFileUrl = maskFileUrl,
                    N = n,
                    Size = size,
                    Quality = quality,
                    ResponseFormat = response_format,
                    OutputFormat = output_format,
                    OutputCompression = output_compression,
                    User = user,
                    DryRun = dryRun,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateEdit(typed);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var sourceFile = (await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken))
                .FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source image from fileUrl.");

            using var form = new MultipartFormDataContent();
            AddFormString(form, "model", typed.Model);
            AddFormString(form, "prompt", typed.Prompt);
            AddFormString(form, "n", typed.N.ToString());
            AddFormString(form, "size", typed.Size);
            AddFormString(form, "quality", typed.Quality);
            AddFormString(form, "response_format", typed.ResponseFormat);
            AddFormString(form, "output_format", typed.OutputFormat);
            AddFormString(form, "output_compression", typed.OutputCompression.ToString());
            AddFormString(form, "user", typed.User);

            var sourceContent = new ByteArrayContent(sourceFile.Contents.ToArray());
            sourceContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(sourceFile.MimeType ?? "image/png");
            form.Add(sourceContent, "image", string.IsNullOrWhiteSpace(sourceFile.Filename) ? "lumenfall-source.png" : sourceFile.Filename);

            if (!string.IsNullOrWhiteSpace(typed.MaskFileUrl))
            {
                var maskFile = (await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.MaskFileUrl, cancellationToken))
                    .FirstOrDefault() ?? throw new InvalidOperationException("Failed to download mask image from maskFileUrl.");

                var maskContent = new ByteArrayContent(maskFile.Contents.ToArray());
                maskContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(maskFile.MimeType ?? "image/png");
                form.Add(maskContent, "mask", string.IsNullOrWhiteSpace(maskFile.Filename) ? "lumenfall-mask.png" : maskFile.Filename);
            }

            var client = serviceProvider.GetRequiredService<LumenfallClient>();
            var path = typed.DryRun ? "images/edits?dryRun=true" : "images/edits";
            var response = await client.PostMultipartAsync(path, form, cancellationToken)
                ?? throw new Exception("Lumenfall returned no response body.");

            if (typed.DryRun)
                return response.ToJsonString().ToJsonCallToolResponse("https://api.lumenfall.ai/openai/v1/images/edits?dryRun=true");

            var links = await UploadImageOutputsAsync(
                serviceProvider,
                requestContext,
                response,
                typed.Filename,
                typed.OutputFormat,
                cancellationToken);

            if (links.Count == 0)
                throw new Exception("Lumenfall image edit completed but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<List<ResourceLinkBlock>> UploadImageOutputsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        JsonNode response,
        string filename,
        string outputFormat,
        CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var b64s = new List<string>();

        CollectImageOutputs(response["data"], urls, b64s);
        if (urls.Count == 0 && b64s.Count == 0)
            CollectImageOutputs(response, urls, b64s);

        var links = new List<ResourceLinkBlock>();
        var baseName = filename.ToOutputFileName();
        var index = 0;

        foreach (var b64 in b64s)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch
            {
                continue;
            }

            if (bytes.Length == 0)
                continue;

            index++;
            var ext = GetImageExtensionFromFormat(outputFormat);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{baseName}-{index}{ext}",
                BinaryData.FromBytes(bytes),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        foreach (var url in urls)
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            index++;
            var ext = GetImageExtension(file.Filename, file.MimeType, outputFormat);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{baseName}-{index}{ext}",
                BinaryData.FromBytes(file.Contents.ToArray()),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        return links;
    }

    private static void CollectImageOutputs(JsonNode? node, HashSet<string> urls, List<string> b64s)
    {
        if (node == null)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    if (kv.Value is JsonValue value && value.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str))
                    {
                        if (kv.Key.Equals("url", StringComparison.OrdinalIgnoreCase)
                            && Uri.TryCreate(str, UriKind.Absolute, out var uri)
                            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            urls.Add(str);
                        }

                        if (kv.Key.Equals("b64_json", StringComparison.OrdinalIgnoreCase))
                            b64s.Add(str);
                    }

                    CollectImageOutputs(kv.Value, urls, b64s);
                }

                break;

            case JsonArray arr:
                foreach (var item in arr)
                    CollectImageOutputs(item, urls, b64s);

                break;
        }
    }

    private static void ValidateGenerate(LumenfallImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.N is < 1 or > 10)
            throw new ValidationException("n must be between 1 and 10.");

        ValidateCommonImage(input.ResponseFormat, input.OutputFormat, input.OutputCompression);
    }

    private static void ValidateEdit(LumenfallImageEditRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.FileUrl))
            throw new ValidationException("fileUrl is required.");

        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.N is < 1 or > 10)
            throw new ValidationException("n must be between 1 and 10.");

        ValidateCommonImage(input.ResponseFormat, input.OutputFormat, input.OutputCompression);
    }

    private static void ValidateCommonImage(string responseFormat, string outputFormat, int outputCompression)
    {
        if (responseFormat is not ("url" or "b64_json"))
            throw new ValidationException("response_format must be one of: url, b64_json.");

        if (outputFormat is not ("png" or "jpeg" or "gif" or "webp" or "avif"))
            throw new ValidationException("output_format must be one of: png, jpeg, gif, webp, avif.");

        if (outputCompression is < 1 or > 100)
            throw new ValidationException("output_compression must be between 1 and 100.");
    }

    private static void AddFormString(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            form.Add(new StringContent(value), name);
    }

    private static string GetImageExtension(string? filename, string? mimeType, string outputFormat)
    {
        var ext = Path.GetExtension(filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/avif" => ".avif",
            _ => GetImageExtensionFromFormat(outputFormat)
        };
    }

    private static string GetImageExtensionFromFormat(string outputFormat)
        => outputFormat.ToLowerInvariant() switch
        {
            "jpeg" => ".jpg",
            "webp" => ".webp",
            "gif" => ".gif",
            "avif" => ".avif",
            _ => ".png"
        };
}

[Description("Please confirm the Lumenfall image generation request details.")]
public sealed class LumenfallImageGenerateRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Text prompt for image generation.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Lumenfall image model.")]
    public string Model { get; set; } = "gemini-3-pro-image";

    [JsonPropertyName("n")]
    [Range(1, 10)]
    [Description("Number of images to generate (1-10).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("size")]
    [Required]
    [Description("Output size.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("quality")]
    [Required]
    [Description("Image quality.")]
    public string Quality { get; set; } = "standard";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format: url or b64_json.")]
    public string ResponseFormat { get; set; } = "url";

    [JsonPropertyName("output_format")]
    [Required]
    [Description("Output image format: png, jpeg, gif, webp, or avif.")]
    public string OutputFormat { get; set; } = "png";

    [JsonPropertyName("output_compression")]
    [Range(1, 100)]
    [Description("Output compression for lossy formats (1-100).")]
    public int OutputCompression { get; set; } = 100;

    [JsonPropertyName("style")]
    [Description("Image style.")]
    public string Style { get; set; } = "vivid";

    [JsonPropertyName("user")]
    [Description("Optional end-user identifier.")]
    public string? User { get; set; }

    [JsonPropertyName("dryRun")]
    [Description("If true, only returns cost estimate.")]
    public bool DryRun { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Lumenfall image edit request details.")]
public sealed class LumenfallImageEditRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Single source image URL (SharePoint/OneDrive/public HTTP).")]
    public string FileUrl { get; set; } = default!;

    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt describing the requested edit.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("model")]
    [Required]
    [Description("Lumenfall image model.")]
    public string Model { get; set; } = "gemini-3-pro-image";

    [JsonPropertyName("maskFileUrl")]
    [Description("Optional mask image URL.")]
    public string? MaskFileUrl { get; set; }

    [JsonPropertyName("n")]
    [Range(1, 10)]
    [Description("Number of images to generate (1-10).")]
    public int N { get; set; } = 1;

    [JsonPropertyName("size")]
    [Required]
    [Description("Output size.")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("quality")]
    [Required]
    [Description("Image quality.")]
    public string Quality { get; set; } = "auto";

    [JsonPropertyName("response_format")]
    [Required]
    [Description("Response format: url or b64_json.")]
    public string ResponseFormat { get; set; } = "url";

    [JsonPropertyName("output_format")]
    [Required]
    [Description("Output image format: png, jpeg, gif, webp, or avif.")]
    public string OutputFormat { get; set; } = "png";

    [JsonPropertyName("output_compression")]
    [Range(1, 100)]
    [Description("Output compression for lossy formats (1-100).")]
    public int OutputCompression { get; set; } = 100;

    [JsonPropertyName("user")]
    [Description("Optional end-user identifier.")]
    public string? User { get; set; }

    [JsonPropertyName("dryRun")]
    [Description("If true, only returns cost estimate.")]
    public bool DryRun { get; set; }

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename without extension.")]
    public string Filename { get; set; } = default!;
}

