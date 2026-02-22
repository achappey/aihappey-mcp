using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
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

namespace MCPhappey.Tools.Picsart;

internal static class PicsartDefaults
{
    public const int PollIntervalSeconds = 2;
    public const int MaxWaitSeconds = 300;
    public const string Text2ImageModel = "urn:air:sdxl:model:fluxai:flux_kontext_max@1";
    public const string LogoModel = "urn:air:ideogram:model:ideogram:ideogram@2";
}

public static class PicsartImages
{
    private const string ApiBaseUrl = "https://genai-api.picsart.io";
    private const int DefaultPollIntervalSeconds = PicsartDefaults.PollIntervalSeconds;
    private const int DefaultMaxWaitSeconds = PicsartDefaults.MaxWaitSeconds;
    private const string DefaultText2ImageModel = PicsartDefaults.Text2ImageModel;
    private const string DefaultLogoModel = PicsartDefaults.LogoModel;

    [Description("Generate image(s) with Picsart Text2Image, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Picsart Text2Image", Name = "picsart_text2image_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Picsart_Text2Image_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the image to generate.")] string prompt,
        [Description("Desired image width (64-1024).")][Range(64, 1024)] int width = 1024,
        [Description("Desired image height (64-1024).")][Range(64, 1024)] int height = 1024,
        [Description("Number of images to generate (1-10).")][Range(1, 10)] int count = 2,
        [Description("Optional Picsart model identifier.")] string? model = DefaultText2ImageModel,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new PicsartText2ImageRequest
                {
                    Prompt = prompt,
                    Width = width,
                    Height = height,
                    Count = count,
                    Model = model,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateCommonRequest(typed.Prompt, typed.Width, typed.Height, typed.Count, typed.PollIntervalSeconds, typed.MaxWaitSeconds);

            var payload = new JsonObject
            {
                ["prompt"] = typed.Prompt,
                ["width"] = typed.Width,
                ["height"] = typed.Height,
                ["count"] = typed.Count
            };

            if (!string.IsNullOrWhiteSpace(typed.Model))
                payload["model"] = typed.Model;

            var inferenceId = await CreateInferenceFromJsonAsync(serviceProvider, "/v1/text2image", payload, cancellationToken);
            var resultUrls = await PollForResultUrlsAsync(serviceProvider, $"/v1/text2image/inferences", inferenceId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, resultUrls, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Picsart Text2Image succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Generate sticker(s) with Picsart Text2Sticker, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Picsart Text2Sticker", Name = "picsart_text2sticker_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Picsart_Text2Sticker_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the sticker to generate.")] string prompt,
        [Description("Desired image width (64-1024).")][Range(64, 1024)] int width = 1024,
        [Description("Desired image height (64-1024).")][Range(64, 1024)] int height = 1024,
        [Description("Number of stickers to generate (1-10).")][Range(1, 10)] int count = 2,
        [Description("Optional Picsart model identifier.")] string? model = DefaultText2ImageModel,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new PicsartText2StickerRequest
                {
                    Prompt = prompt,
                    Width = width,
                    Height = height,
                    Count = count,
                    Model = model,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateCommonRequest(typed.Prompt, typed.Width, typed.Height, typed.Count, typed.PollIntervalSeconds, typed.MaxWaitSeconds);

            var payload = new JsonObject
            {
                ["prompt"] = typed.Prompt,
                ["width"] = typed.Width,
                ["height"] = typed.Height,
                ["count"] = typed.Count
            };

            if (!string.IsNullOrWhiteSpace(typed.Model))
                payload["model"] = typed.Model;

            var inferenceId = await CreateInferenceFromJsonAsync(serviceProvider, "/v1/text2sticker", payload, cancellationToken);
            var resultUrls = await PollForResultUrlsAsync(serviceProvider, $"/v1/text2sticker/inferences", inferenceId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, resultUrls, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Picsart Text2Sticker succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Generate sticker(s) with Picsart Text2Sticker laser engraving effect, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Picsart Text2Sticker Laser", Name = "picsart_text2sticker_laser_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Picsart_Text2Sticker_Laser_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the sticker to generate.")] string prompt,
        [Description("Desired image width (64-1024).")][Range(64, 1024)] int width = 1024,
        [Description("Desired image height (64-1024).")][Range(64, 1024)] int height = 1024,
        [Description("Number of stickers to generate (1-10).")][Range(1, 10)] int count = 2,
        [Description("Laser engrave color (hex or name). Default: black.")] string? engraveColor = "black",
        [Description("Background color (hex or name). Default: white.")] string? backgroundColor = "white",
        [Description("Output format: JPG, PNG, WEBP, SVG. Default: JPG.")] string? format = "JPG",
        [Description("Optional Picsart model identifier.")] string? model = DefaultText2ImageModel,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new PicsartText2StickerLaserRequest
                {
                    Prompt = prompt,
                    Width = width,
                    Height = height,
                    Count = count,
                    EngraveColor = engraveColor,
                    BackgroundColor = backgroundColor,
                    Format = format,
                    Model = model,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateCommonRequest(typed.Prompt, typed.Width, typed.Height, typed.Count, typed.PollIntervalSeconds, typed.MaxWaitSeconds);

            var payload = new JsonObject
            {
                ["prompt"] = typed.Prompt,
                ["width"] = typed.Width,
                ["height"] = typed.Height,
                ["count"] = typed.Count
            };

            if (!string.IsNullOrWhiteSpace(typed.EngraveColor))
                payload["engrave_color"] = typed.EngraveColor;

            if (!string.IsNullOrWhiteSpace(typed.BackgroundColor))
                payload["background_color"] = typed.BackgroundColor;

            if (!string.IsNullOrWhiteSpace(typed.Format))
                payload["format"] = typed.Format;

            if (!string.IsNullOrWhiteSpace(typed.Model))
                payload["model"] = typed.Model;

            var inferenceId = await CreateInferenceFromJsonAsync(serviceProvider, "/v1/text2sticker/laserengraving", payload, cancellationToken);
            var resultUrls = await PollForResultUrlsAsync(serviceProvider, $"/v1/text2sticker/inferences", inferenceId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, resultUrls, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Picsart Text2Sticker laser succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    [Description("Generate logo(s) with Picsart Logo Generator, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(Title = "Picsart Logo Generator", Name = "picsart_logo_generate", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Picsart_Logo_Generate(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Brand or company name.")] string brandName,
        [Description("Business description.")] string businessDescription,
        [Description("Color tone: Auto, Gray, Blue, Pink, Orange, Brown, Yellow, Green, Purple, Red.")] string colorTone = "Auto",
        [Description("Logo description details.")] string? logoDescription = null,
        [Description("Reference logo URL.")] string? referenceImageUrl = null,
        [Description("Number of logos to generate (1-10).")][Range(1, 10)] int count = 2,
        [Description("Optional Picsart model identifier.")] string? model = DefaultLogoModel,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename base without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new PicsartLogoRequest
                {
                    BrandName = brandName,
                    BusinessDescription = businessDescription,
                    ColorTone = colorTone,
                    LogoDescription = logoDescription,
                    ReferenceImageUrl = referenceImageUrl,
                    Count = count,
                    Model = model,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            ValidateLogoRequest(typed);

            using var content = new MultipartFormDataContent();
            AddFormString(content, "brand_name", typed.BrandName);
            AddFormString(content, "business_description", typed.BusinessDescription);
            AddFormString(content, "color_tone", typed.ColorTone);
            AddFormString(content, "logo_description", typed.LogoDescription);
            AddFormString(content, "reference_image_url", typed.ReferenceImageUrl);
            AddFormString(content, "count", typed.Count.ToString());
            AddFormString(content, "model", typed.Model);

            var inferenceId = await CreateInferenceFromFormAsync(serviceProvider, "/v1/logo", content, cancellationToken);
            var resultUrls = await PollForResultUrlsAsync(serviceProvider, $"/v1/logo/inferences", inferenceId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
            var links = await DownloadUploadFromUrlsAsync(serviceProvider, requestContext, resultUrls, typed.Filename, cancellationToken);

            if (links.Count == 0)
                throw new Exception("Picsart logo generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static void ValidateCommonRequest(string prompt, int width, int height, int count, int pollIntervalSeconds, int maxWaitSeconds)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ValidationException("prompt is required.");

        if (width < 64 || width > 1024)
            throw new ValidationException("width must be between 64 and 1024.");

        if (height < 64 || height > 1024)
            throw new ValidationException("height must be between 64 and 1024.");

        if (count < 1 || count > 10)
            throw new ValidationException("count must be between 1 and 10.");

        if (pollIntervalSeconds < 1 || pollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (maxWaitSeconds < 30 || maxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    private static void ValidateLogoRequest(PicsartLogoRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.BrandName))
            throw new ValidationException("brandName is required.");

        if (string.IsNullOrWhiteSpace(input.BusinessDescription))
            throw new ValidationException("businessDescription is required.");

        if (input.Count < 1 || input.Count > 10)
            throw new ValidationException("count must be between 1 and 10.");

        if (input.PollIntervalSeconds < 1 || input.PollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (input.MaxWaitSeconds < 30 || input.MaxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    private static async Task<string> CreateInferenceFromJsonAsync(
        IServiceProvider serviceProvider,
        string path,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<PicsartSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        client.DefaultRequestHeaders.Add("X-Picsart-API-Key", settings.ApiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {raw}");

        var node = JsonNode.Parse(raw) ?? new JsonObject();
        var inferenceId = ExtractInferenceId(node);

        if (string.IsNullOrWhiteSpace(inferenceId))
            throw new Exception("Picsart did not return inference_id.");

        return inferenceId;
    }

    private static async Task<string> CreateInferenceFromFormAsync(
        IServiceProvider serviceProvider,
        string path,
        MultipartFormDataContent form,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<PicsartSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        client.DefaultRequestHeaders.Add("X-Picsart-API-Key", settings.ApiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = form
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {raw}");

        var node = JsonNode.Parse(raw) ?? new JsonObject();
        var inferenceId = ExtractInferenceId(node);

        if (string.IsNullOrWhiteSpace(inferenceId))
            throw new Exception("Picsart did not return inference_id.");

        return inferenceId;
    }

    private static async Task<List<string>> PollForResultUrlsAsync(
        IServiceProvider serviceProvider,
        string path,
        string inferenceId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        var settings = serviceProvider.GetRequiredService<PicsartSettings>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        client.DefaultRequestHeaders.Add("X-Picsart-API-Key", settings.ApiKey);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}/{inferenceId}");
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var raw = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Polling failed ({response.StatusCode}): {raw}");

            var node = JsonNode.Parse(raw) ?? new JsonObject();
            var status = node["status"]?.GetValue<string>()?.Trim().ToLowerInvariant();
            var urls = ExtractResultUrls(node);

            if (IsDoneStatus(status))
                return urls;

            if (IsErrorStatus(status))
                throw new Exception($"Picsart inference {inferenceId} failed with status '{status}'.");

            if (urls.Count > 0)
                return urls;

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"Picsart inference {inferenceId} did not complete within {maxWaitSeconds} seconds.");
    }

    private static string? ExtractInferenceId(JsonNode node)
    {
        var direct = node["inference_id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        var dataId = node["data"]?["inference_id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(dataId))
            return dataId;

        var id = node["id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        var dataNestedId = node["data"]?["id"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(dataNestedId))
            return dataNestedId;

        return null;
    }

    private static List<string> ExtractResultUrls(JsonNode response)
    {
        var urls = new List<string>();
        if (response["data"] is JsonArray data)
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

    private static void AddFormString(MultipartFormDataContent form, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        form.Add(new StringContent(value), name);
    }

    private static bool IsDoneStatus(string? status)
        => status is "done" or "completed" or "success" or "succeeded";

    private static bool IsErrorStatus(string? status)
        => status is "error" or "failed" or "canceled" or "cancelled";

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

[Description("Please confirm the Picsart Text2Image request details.")]
public sealed class PicsartText2ImageRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt describing the image to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("width")]
    [Range(64, 1024)]
    [Description("Desired image width (64-1024).")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    [Range(64, 1024)]
    [Description("Desired image height (64-1024).")]
    public int Height { get; set; } = 1024;

    [JsonPropertyName("count")]
    [Range(1, 10)]
    [Description("Number of images to generate (1-10).")]
    public int Count { get; set; } = 2;

    [JsonPropertyName("model")]
    [Description("Optional Picsart model identifier.")]
    public string? Model { get; set; } = PicsartDefaults.Text2ImageModel;

    [JsonPropertyName("pollIntervalSeconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollIntervalSeconds { get; set; } = PicsartDefaults.PollIntervalSeconds;

    [JsonPropertyName("maxWaitSeconds")]
    [Range(30, 3600)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = PicsartDefaults.MaxWaitSeconds;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Picsart Text2Sticker request details.")]
public sealed class PicsartText2StickerRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt describing the sticker to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("width")]
    [Range(64, 1024)]
    [Description("Desired image width (64-1024).")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    [Range(64, 1024)]
    [Description("Desired image height (64-1024).")]
    public int Height { get; set; } = 1024;

    [JsonPropertyName("count")]
    [Range(1, 10)]
    [Description("Number of stickers to generate (1-10).")]
    public int Count { get; set; } = 2;

    [JsonPropertyName("model")]
    [Description("Optional Picsart model identifier.")]
    public string? Model { get; set; } = PicsartDefaults.Text2ImageModel;

    [JsonPropertyName("pollIntervalSeconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollIntervalSeconds { get; set; } = PicsartDefaults.PollIntervalSeconds;

    [JsonPropertyName("maxWaitSeconds")]
    [Range(30, 3600)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = PicsartDefaults.MaxWaitSeconds;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Picsart Text2Sticker laser request details.")]
public sealed class PicsartText2StickerLaserRequest
{
    [JsonPropertyName("prompt")]
    [Required]
    [Description("Prompt describing the sticker to generate.")]
    public string Prompt { get; set; } = default!;

    [JsonPropertyName("width")]
    [Range(64, 1024)]
    [Description("Desired image width (64-1024).")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    [Range(64, 1024)]
    [Description("Desired image height (64-1024).")]
    public int Height { get; set; } = 1024;

    [JsonPropertyName("count")]
    [Range(1, 10)]
    [Description("Number of stickers to generate (1-10).")]
    public int Count { get; set; } = 2;

    [JsonPropertyName("engraveColor")]
    [Description("Laser engrave color.")]
    public string? EngraveColor { get; set; } = "black";

    [JsonPropertyName("backgroundColor")]
    [Description("Background color.")]
    public string? BackgroundColor { get; set; } = "white";

    [JsonPropertyName("format")]
    [Description("Output format: JPG, PNG, WEBP, SVG.")]
    public string? Format { get; set; } = "JPG";

    [JsonPropertyName("model")]
    [Description("Optional Picsart model identifier.")]
    public string? Model { get; set; } = PicsartDefaults.Text2ImageModel;

    [JsonPropertyName("pollIntervalSeconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollIntervalSeconds { get; set; } = PicsartDefaults.PollIntervalSeconds;

    [JsonPropertyName("maxWaitSeconds")]
    [Range(30, 3600)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = PicsartDefaults.MaxWaitSeconds;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;
}

[Description("Please confirm the Picsart Logo Generator request details.")]
public sealed class PicsartLogoRequest
{
    [JsonPropertyName("brandName")]
    [Required]
    [Description("Brand or company name.")]
    public string BrandName { get; set; } = default!;

    [JsonPropertyName("businessDescription")]
    [Required]
    [Description("Business description.")]
    public string BusinessDescription { get; set; } = default!;

    [JsonPropertyName("colorTone")]
    [Description("Color tone (Auto, Gray, Blue, Pink, Orange, Brown, Yellow, Green, Purple, Red).")]
    public string ColorTone { get; set; } = "Auto";

    [JsonPropertyName("logoDescription")]
    [Description("Logo description details.")]
    public string? LogoDescription { get; set; }

    [JsonPropertyName("referenceImageUrl")]
    [Description("Reference logo URL.")]
    public string? ReferenceImageUrl { get; set; }

    [JsonPropertyName("count")]
    [Range(1, 10)]
    [Description("Number of logos to generate (1-10).")]
    public int Count { get; set; } = 2;

    [JsonPropertyName("model")]
    [Description("Optional Picsart model identifier.")]
    public string? Model { get; set; } = PicsartDefaults.LogoModel;

    [JsonPropertyName("pollIntervalSeconds")]
    [Range(1, 60)]
    [Description("Polling interval in seconds.")]
    public int PollIntervalSeconds { get; set; } = PicsartDefaults.PollIntervalSeconds;

    [JsonPropertyName("maxWaitSeconds")]
    [Range(30, 3600)]
    [Description("Maximum total wait time in seconds.")]
    public int MaxWaitSeconds { get; set; } = PicsartDefaults.MaxWaitSeconds;

    [JsonPropertyName("filename")]
    [Required]
    [Description("Output filename base without extension.")]
    public string Filename { get; set; } = default!;
}
