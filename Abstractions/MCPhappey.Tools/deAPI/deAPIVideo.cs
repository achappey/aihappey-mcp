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

public static class deAPIVideo
{
    private const string BaseUrl = "https://api.deapi.ai";
    private const string Txt2VideoPath = "/api/v1/client/txt2video";
    private const string Txt2VideoPricePath = "/api/v1/client/txt2video/price-calculation";
    private const string Img2VideoPath = "/api/v1/client/img2video";
    private const string Img2VideoPricePath = "/api/v1/client/img2video/price-calculation";
    private const int DefaultPollIntervalSeconds = 2;
    private const int DefaultMaxWaitSeconds = 300;

    [Description("Generate video from text using deAPI, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "deAPI Text-to-Video",
        Name = "deapi_video_text_to_video",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Video_TextToVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the video to generate.")] string prompt,
        [Description("deAPI model slug for text-to-video.")] string model,
        [Description("Video width in pixels.")] int width,
        [Description("Video height in pixels.")] int height,
        [Description("Guidance scale.")] double guidance,
        [Description("Number of inference steps.")] int steps,
        [Description("Number of frames to generate.")] int frames,
        [Description("Frames per second.")] int? fps = null,
        [Description("Random seed.")] int seed = 42,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new deAPITextToVideoRequest
                {
                    Prompt = prompt,
                    Model = model,
                    Width = width,
                    Height = height,
                    Guidance = guidance,
                    Steps = steps,
                    Frames = frames,
                    Fps = fps,
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
                return "Video generation canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidateTextToVideoRequest(typed);

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var payload = new
            {
                prompt = typed.Prompt,
                negative_prompt = string.IsNullOrWhiteSpace(typed.NegativePrompt) ? null : typed.NegativePrompt,
                model = typed.Model,
                width = typed.Width,
                height = typed.Height,
                guidance = typed.Guidance,
                steps = typed.Steps,
                frames = typed.Frames,
                fps = typed.Fps,
                seed = typed.Seed
            };

            using var client = clientFactory.CreateClient();
            var requestId = await SubmitJsonJobAsync(client, settings.ApiKey, Txt2VideoPath, payload, cancellationToken);

            var resultUrls = await PollForResultUrlsAsync(
                client,
                settings.ApiKey,
                requestId,
                typed.PollIntervalSeconds,
                typed.MaxWaitSeconds,
                cancellationToken);

            return await DownloadUploadAndReturnLinksAsync(
                serviceProvider,
                requestContext,
                resultUrls,
                typed.Filename,
                cancellationToken);
        });

    [Description("Calculate deAPI text-to-video price.")]
    [McpServerTool(
        Title = "deAPI Text-to-Video Price Calculation",
        Name = "deapi_video_text_to_video_price",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Video_TextToVideoPrice(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("deAPI model slug for text-to-video.")] string model,
        [Description("Video width in pixels.")] int width,
        [Description("Video height in pixels.")] int height,
        [Description("Number of inference steps.")] int steps,
        [Description("Number of frames to generate.")] int frames,
        [Description("Frames per second.")] int? fps = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ValidatePriceRequest(model, width, height, steps, frames);

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var payload = new
            {
                model,
                width,
                height,
                steps,
                frames,
                fps
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Txt2VideoPricePath}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            return json.ToJsonCallToolResponse($"{BaseUrl}{Txt2VideoPricePath}");
        });

    [Description("Generate video from a single fileUrl first-frame image using deAPI, always confirm via elicitation, upload outputs to SharePoint/OneDrive, and return only resource link blocks.")]
    [McpServerTool(
        Title = "deAPI Image-to-Video",
        Name = "deapi_video_image_to_video",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Video_ImageToVideo(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Prompt describing the video to generate.")] string prompt,
        [Description("Input first-frame image URL (SharePoint/OneDrive/HTTP).")]
        string fileUrl,
        [Description("deAPI model slug for image-to-video.")] string model,
        [Description("Video width in pixels.")] int width,
        [Description("Video height in pixels.")] int height,
        [Description("Guidance scale.")] double guidance,
        [Description("Number of inference steps.")] int steps,
        [Description("Number of frames to generate.")] int frames,
        [Description("Frames per second.")] int? fps = null,
        [Description("Random seed.")] int seed = 42,
        [Description("Optional negative prompt.")] string? negative_prompt = null,
        [Description("Polling interval in seconds.")][Range(1, 60)] int pollIntervalSeconds = DefaultPollIntervalSeconds,
        [Description("Maximum total wait time in seconds.")][Range(30, 3600)] int maxWaitSeconds = DefaultMaxWaitSeconds,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new deAPIImageToVideoRequest
                {
                    Prompt = prompt,
                    FileUrl = fileUrl,
                    Model = model,
                    Width = width,
                    Height = height,
                    Guidance = guidance,
                    Steps = steps,
                    Frames = frames,
                    Fps = fps,
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
                return "Video generation canceled: confirmation text must be 'GENERATE'.".ToErrorCallToolResponse();

            ValidateImageToVideoRequest(typed);

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, typed.FileUrl, cancellationToken);
            var source = files.FirstOrDefault() ?? throw new Exception("Failed to download fileUrl content.");

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Img2VideoPath}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

            using var form = new MultipartFormDataContent();
            AddFormString(form, "prompt", typed.Prompt);
            AddFormString(form, "negative_prompt", typed.NegativePrompt);
            AddFormString(form, "model", typed.Model);
            AddFormString(form, "width", typed.Width.ToString());
            AddFormString(form, "height", typed.Height.ToString());
            AddFormString(form, "guidance", typed.Guidance.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddFormString(form, "steps", typed.Steps.ToString());
            AddFormString(form, "frames", typed.Frames.ToString());
            AddFormString(form, "seed", typed.Seed.ToString());
            if (typed.Fps.HasValue) AddFormString(form, "fps", typed.Fps.Value.ToString());

            var contentType = string.IsNullOrWhiteSpace(source.MimeType) ? "application/octet-stream" : source.MimeType;
            var imageContent = new ByteArrayContent(source.Contents.ToArray());
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var sourceName = string.IsNullOrWhiteSpace(source.Filename) ? "first_frame_image.png" : source.Filename;
            form.Add(imageContent, "first_frame_image", sourceName);

            req.Content = form;

            using var resp = await client.SendAsync(req, cancellationToken);
            var submitJson = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {submitJson}");

            var requestId = ExtractRequestId(submitJson);
            var resultUrls = await PollForResultUrlsAsync(
                client,
                settings.ApiKey,
                requestId,
                typed.PollIntervalSeconds,
                typed.MaxWaitSeconds,
                cancellationToken);

            return await DownloadUploadAndReturnLinksAsync(
                serviceProvider,
                requestContext,
                resultUrls,
                typed.Filename,
                cancellationToken);
        });

    [Description("Calculate deAPI image-to-video price.")]
    [McpServerTool(
        Title = "deAPI Image-to-Video Price Calculation",
        Name = "deapi_video_image_to_video_price",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> deAPI_Video_ImageToVideoPrice(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("deAPI model slug for image-to-video.")] string model,
        [Description("Video width in pixels.")] int width,
        [Description("Video height in pixels.")] int height,
        [Description("Number of inference steps.")] int steps,
        [Description("Number of frames to generate.")] int frames,
        [Description("Frames per second.")] int? fps = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ValidatePriceRequest(model, width, height, steps, frames);

            var settings = serviceProvider.GetRequiredService<deAPISettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            var payload = new
            {
                model,
                width,
                height,
                steps,
                frames,
                fps
            };

            using var client = clientFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{Img2VideoPricePath}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            return json.ToJsonCallToolResponse($"{BaseUrl}{Img2VideoPricePath}");
        });

    private static async Task<string> SubmitJsonJobAsync(
        HttpClient client,
        string apiKey,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);

        using var resp = await client.SendAsync(req, cancellationToken);
        var submitJson = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {submitJson}");

        return ExtractRequestId(submitJson);
    }

    private static string ExtractRequestId(string submitJson)
    {
        using var submitDoc = JsonDocument.Parse(submitJson);
        var requestId = submitDoc.RootElement
            .GetProperty("data")
            .GetProperty("request_id")
            .GetString();

        if (string.IsNullOrWhiteSpace(requestId))
            throw new Exception("deAPI did not return request_id.");

        return requestId;
    }

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
            var status = data.TryGetProperty("status", out var statusEl)
                ? statusEl.GetString()?.Trim().ToLowerInvariant()
                : null;

            if (status == "done")
            {
                var urls = ExtractResultUrls(data);
                if (urls.Count == 0)
                    throw new Exception($"deAPI job {requestId} completed without downloadable video URL(s).");

                return urls;
            }

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
            if (!string.IsNullOrWhiteSpace(resultUrl))
                urls.Add(resultUrl);
        }

        if (data.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in resultEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var asString = item.GetString();
                    if (!string.IsNullOrWhiteSpace(asString))
                        urls.Add(asString);

                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("url", out var urlEl)
                    && urlEl.ValueKind == JsonValueKind.String)
                {
                    var nestedUrl = urlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(nestedUrl))
                        urls.Add(nestedUrl);
                }
            }
        }

        return urls;
    }

    private static async Task<CallToolResult?> DownloadUploadAndReturnLinksAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        IEnumerable<string> resultUrls,
        string filenameBase,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var links = new List<ResourceLinkBlock>();
        var baseName = filenameBase.ToOutputFileName();
        var index = 0;

        foreach (var url in resultUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            index++;

            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
            var file = files.FirstOrDefault();
            if (file == null)
                continue;

            var ext = GetVideoExtension(file.Filename, file.MimeType);
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                $"{baseName}-{index}{ext}",
                BinaryData.FromBytes(file.Contents.ToArray()),
                cancellationToken);

            if (uploaded != null)
                links.Add(uploaded);
        }

        if (links.Count == 0)
            throw new Exception("Video generation succeeded but no outputs could be uploaded.");

        return links.ToResourceLinkCallToolResponse();
    }

    private static void AddFormString(MultipartFormDataContent form, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            form.Add(new StringContent(value), name);
    }

    private static void ValidateTextToVideoRequest(deAPITextToVideoRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        ValidateVideoGenerationCore(input.Model, input.Width, input.Height, input.Guidance, input.Steps, input.Frames, input.PollIntervalSeconds, input.MaxWaitSeconds);
    }

    private static void ValidateImageToVideoRequest(deAPIImageToVideoRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Prompt))
            throw new ValidationException("prompt is required.");

        if (string.IsNullOrWhiteSpace(input.FileUrl))
            throw new ValidationException("fileUrl is required.");

        ValidateVideoGenerationCore(input.Model, input.Width, input.Height, input.Guidance, input.Steps, input.Frames, input.PollIntervalSeconds, input.MaxWaitSeconds);
    }

    private static void ValidateVideoGenerationCore(
        string model,
        int width,
        int height,
        double guidance,
        int steps,
        int frames,
        int pollIntervalSeconds,
        int maxWaitSeconds)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        if (width <= 0)
            throw new ValidationException("width must be greater than 0.");

        if (height <= 0)
            throw new ValidationException("height must be greater than 0.");

        if (guidance <= 0)
            throw new ValidationException("guidance must be greater than 0.");

        if (steps <= 0)
            throw new ValidationException("steps must be greater than 0.");

        if (frames <= 0)
            throw new ValidationException("frames must be greater than 0.");

        if (pollIntervalSeconds < 1 || pollIntervalSeconds > 60)
            throw new ValidationException("pollIntervalSeconds must be between 1 and 60.");

        if (maxWaitSeconds < 30 || maxWaitSeconds > 3600)
            throw new ValidationException("maxWaitSeconds must be between 30 and 3600.");
    }

    private static void ValidatePriceRequest(string model, int width, int height, int steps, int frames)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ValidationException("model is required.");

        if (width <= 0)
            throw new ValidationException("width must be greater than 0.");

        if (height <= 0)
            throw new ValidationException("height must be greater than 0.");

        if (steps <= 0)
            throw new ValidationException("steps must be greater than 0.");

        if (frames <= 0)
            throw new ValidationException("frames must be greater than 0.");
    }

    private static string GetVideoExtension(string? filename, string? mimeType)
    {
        var ext = Path.GetExtension(filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            "video/x-matroska" => ".mkv",
            _ => ".mp4"
        };
    }

    [Description("Please confirm the deAPI text-to-video request details.")]
    public sealed class deAPITextToVideoRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Optional negative prompt.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("deAPI model slug for text-to-video.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("width")]
        [Required]
        [Description("Video width in pixels.")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        [Required]
        [Description("Video height in pixels.")]
        public int Height { get; set; }

        [JsonPropertyName("guidance")]
        [Required]
        [Description("Guidance scale.")]
        public double Guidance { get; set; }

        [JsonPropertyName("steps")]
        [Required]
        [Description("Number of inference steps.")]
        public int Steps { get; set; }

        [JsonPropertyName("frames")]
        [Required]
        [Description("Number of frames to generate.")]
        public int Frames { get; set; }

        [JsonPropertyName("fps")]
        [Description("Frames per second.")]
        public int? Fps { get; set; }

        [JsonPropertyName("seed")]
        [Required]
        [Description("Random seed.")]
        public int Seed { get; set; } = 42;

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

    [Description("Please confirm the deAPI image-to-video request details.")]
    public sealed class deAPIImageToVideoRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Input first-frame image URL (SharePoint/OneDrive/HTTP).")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Optional negative prompt.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("deAPI model slug for image-to-video.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("width")]
        [Required]
        [Description("Video width in pixels.")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        [Required]
        [Description("Video height in pixels.")]
        public int Height { get; set; }

        [JsonPropertyName("guidance")]
        [Required]
        [Description("Guidance scale.")]
        public double Guidance { get; set; }

        [JsonPropertyName("steps")]
        [Required]
        [Description("Number of inference steps.")]
        public int Steps { get; set; }

        [JsonPropertyName("frames")]
        [Required]
        [Description("Number of frames to generate.")]
        public int Frames { get; set; }

        [JsonPropertyName("fps")]
        [Description("Frames per second.")]
        public int? Fps { get; set; }

        [JsonPropertyName("seed")]
        [Required]
        [Description("Random seed.")]
        public int Seed { get; set; } = 42;

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
