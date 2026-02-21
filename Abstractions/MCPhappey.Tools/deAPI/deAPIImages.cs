using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.deAPI;

public static class deAPIImages
{
    private const string BaseUrl = "https://api.deapi.ai";
    private const string Txt2ImgPath = "/api/v1/client/txt2img";
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 300;

    [Description("Generate image(s) from text using deAPI, always confirm via elicitation, upload output(s) to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "deAPI Text-to-Image",
        Name = "deapi_images_text_to_image",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Images_TextToImage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Text prompt describing the image to generate.")] string prompt,
        [Description("deAPI model slug for text-to-image.")] string model = "Flux1schnell",
        [Description("Image width in pixels.")] int width = 512,
        [Description("Image height in pixels.")] int height = 512,
        [Description("Guidance scale.")] double guidance = 3.5,
        [Description("Number of inference steps.")] int steps = 4,
        [Description("Random seed (-1 for random).")][Range(-1, int.MaxValue)] int seed = -1,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new deAPIImageGenerateRequest
                {
                    Prompt = prompt,
                    Model = model,
                    Width = width,
                    Height = height,
                    Guidance = guidance,
                    Steps = steps,
                    Seed = seed,
                    NegativePrompt = negative_prompt,
                    PollIntervalSeconds = pollIntervalSeconds,
                    MaxWaitSeconds = maxWaitSeconds,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName(),
                    Confirmation = "GENERATE"
                },
                cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "No input data provided".ToErrorCallToolResponse();

            if (!string.Equals(typed.Confirmation?.Trim(), "GENERATE", StringComparison.OrdinalIgnoreCase))
                return "Image generation canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidateImageRequest(typed);

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var payload = new
            {
                prompt = typed.Prompt,
                negative_prompt = string.IsNullOrWhiteSpace(typed.NegativePrompt) ? null : typed.NegativePrompt,
                model = typed.Model,
                width = typed.Width,
                height = typed.Height,
                guidance = typed.Guidance,
                steps = typed.Steps,
                seed = typed.Seed
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Txt2ImgPath}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var submitJson = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {submitJson}");

            using var submitDoc = JsonDocument.Parse(submitJson);
            var requestId = submitDoc.RootElement
                .GetProperty("data")
                .GetProperty("request_id")
                .GetString();

            if (string.IsNullOrWhiteSpace(requestId))
                throw new Exception("deAPI did not return request_id.");

            var resultUrls = await PollForResultUrlsAsync(client, settings.ApiKey, requestId, typed.PollIntervalSeconds, typed.MaxWaitSeconds, cancellationToken);
            if (resultUrls.Count == 0)
                throw new Exception($"deAPI job {requestId} completed without downloadable image URL(s).");

            var links = new List<ResourceLinkBlock>();
            var baseName = typed.Filename.ToOutputFileName();
            var i = 0;

            foreach (var url in resultUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                i++;

                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                var file = files.FirstOrDefault();
                if (file == null)
                    continue;

                var ext = GetImageExtension(file.Filename, file.MimeType);
                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{baseName}-{i}{ext}",
                    BinaryData.FromBytes(file.Contents.ToArray()),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }

            if (links.Count == 0)
                throw new Exception("Image generation succeeded but no outputs could be uploaded.");

            return links.ToResourceLinkCallToolResponse();
        });

    private static async Task<List<string>> PollForResultUrlsAsync(
        HttpClient client,
        string apiKey,
        string requestId,
        int pollIntervalSeconds,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxWaitSeconds));

        while (!timeoutCts.IsCancellationRequested)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v1/client/request-status/{requestId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var resp = await client.SendAsync(req, timeoutCts.Token);
            var statusJson = await resp.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Polling failed ({resp.StatusCode}): {statusJson}");

            using var doc = JsonDocument.Parse(statusJson);
            var data = doc.RootElement.GetProperty("data");
            var status = data.TryGetProperty("status", out var statusEl) ? statusEl.GetString()?.Trim().ToLowerInvariant() : null;

            if (status == "done")
                return ExtractResultUrls(data);

            if (status == "error")
            {
                var error = data.TryGetProperty("error", out var errorEl) ? errorEl.ToString() : "unknown deAPI error";
                throw new Exception($"deAPI request {requestId} failed: {error}");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), timeoutCts.Token);
        }

        throw new TimeoutException($"deAPI request {requestId} did not complete within {maxWaitSeconds} seconds.");
    }

    private static List<string> ExtractResultUrls(JsonElement data)
    {
        var urls = new List<string>();

        if (data.TryGetProperty("result_url", out var resultUrlEl) && resultUrlEl.ValueKind == JsonValueKind.String)
        {
            var resultUrl = resultUrlEl.GetString();
            if (!string.IsNullOrWhiteSpace(resultUrl)) urls.Add(resultUrl);
        }

        if (data.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var asString = item.GetString();
                    if (!string.IsNullOrWhiteSpace(asString)) urls.Add(asString);
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("url", out var urlEl)
                    && urlEl.ValueKind == JsonValueKind.String)
                {
                    var nestedUrl = urlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nestedUrl)) urls.Add(nestedUrl);
                }
            }
        }

        return urls;
    }

    private static void ValidateImageRequest(deAPIImageGenerateRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.Model))
            throw new ValidationException("model is required.");

        if (input.Width <= 0)
            throw new ValidationException("width must be greater than 0.");

        if (input.Height <= 0)
            throw new ValidationException("height must be greater than 0.");

        if (input.Guidance <= 0)
            throw new ValidationException("guidance must be greater than 0.");

        if (input.Steps <= 0)
            throw new ValidationException("steps must be greater than 0.");

        if (input.Seed < -1)
            throw new ValidationException("seed must be -1 or greater.");

        if (input.PollIntervalSeconds < 1 || input.PollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (input.MaxWaitSeconds < 30 || input.MaxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
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

    [Description("Please confirm the deAPI text-to-image request details.")]
    public sealed class deAPIImageGenerateRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Text prompt for image generation.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Optional negative prompt.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("deAPI model slug for text-to-image.")]
        public string Model { get; set; } = "Flux1schnell";

        [JsonPropertyName("width")]
        [Required]
        [Description("Image width in pixels.")]
        public int Width { get; set; } = 512;

        [JsonPropertyName("height")]
        [Required]
        [Description("Image height in pixels.")]
        public int Height { get; set; } = 512;

        [JsonPropertyName("guidance")]
        [Required]
        [Description("Guidance scale.")]
        public double Guidance { get; set; } = 3.5;

        [JsonPropertyName("steps")]
        [Required]
        [Description("Number of inference steps.")]
        public int Steps { get; set; } = 4;

        [JsonPropertyName("seed")]
        [Required]
        [Description("Random seed (-1 for random).")]
        public int Seed { get; set; } = -1;

        [JsonPropertyName("pollIntervalSeconds")]
        [Required]
        [Description("Polling interval in seconds.")]
        public int PollIntervalSeconds { get; set; } = DefaultPollIntervalSeconds;

        [JsonPropertyName("maxWaitSeconds")]
        [Required]
        [Description("Maximum total wait in seconds.")]
        public int MaxWaitSeconds { get; set; } = DefaultMaxWaitSeconds;

        [JsonPropertyName("filename")]
        [Required]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;

        [JsonPropertyName("confirmation")]
        [Required]
        [Description("Type GENERATE to confirm execution.")]
        public string Confirmation { get; set; } = "GENERATE";
    }
}

