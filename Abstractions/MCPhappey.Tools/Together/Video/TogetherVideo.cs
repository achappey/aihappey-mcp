using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Together.Video;

public static class TogetherVideo
{
    [Description("List all Together AI video models.")]
    [McpServerTool(Title = "List Together AI video models",
        Name = "together_video_list_models",
        ReadOnly = true)]
    public static async Task<CallToolResult?> TogetherVideo_ListModels(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
        {
            using var client = serviceProvider.CreateTogetherClient();

            using var resp = await client.GetAsync("https://api.together.xyz/v1/models", cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var models = (root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : root.GetProperty("data").EnumerateArray())
                .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "video")
                .Select(e => JsonNode.Parse(e.GetRawText())!)
                .ToArray();

            return new JsonObject { ["models"] = new JsonArray(models) };
        }));

    [Description("Create a new video using Together AI video models.")]
    [McpServerTool(Title = "Create Together AI video", Name = "together_video_create",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> TogetherVideo_Create(
        [Description("Prompt describing the video to generate.")] string prompt,
        [Description("Video model to use, e.g. black-forest-labs/FLUX.1-schnell-video")] string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Height of the video.")] int? height = null,
        [Description("Video duration in seconds.")] int? seconds = null,
        [Description("Frames per second.")] int? fps = null,
        [Description("Width of the video.")] int? width = null,
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Optional seed for deterministic generation.")] int? seed = null,
        [Description("Optional list of images to guide video generation, similar to keyframes. Protected links like SharePoint and/or OneDrive are supported.")] IEnumerable<string>? frameImages = null,
        [Description("Optional list of reference images. Unlike frame_images which constrain specific timeline positions, reference images guide the general appearance that should appear consistently across the video. For reference images only public accessible URLs are supported.")]
        IEnumerable<string>? referenceImages = null,
        [Description("Filename (without extension).")] string? filename = null,
        [Description("Wait until complete.")] bool? waitUntilComplete = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        //   await requestContext.WithStructuredContent(async () =>
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            /*    if (frameImages?.Any() == true && frameImages.Count() > 2)
                {
                    throw new Exception("Max 2 frame images supported");
                }*/

            List<string> items = [];

            foreach (var item in frameImages ?? [])
            {
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, item, cancellationToken);

                items.Add(Convert.ToBase64String(files.FirstOrDefault()?.Contents));
            }

            filename ??= requestContext.ToOutputFileName("mp4");

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new TogetherNewVideo
            {
                Prompt = prompt,
                Model = model,
                Seconds = seconds,
                Fps = fps,
                Width = width,
                Height = height,
                WaitUntilCompleted = waitUntilComplete ?? true,
                NegativePrompt = negativePrompt,
                Seed = seed,
                Filename = filename
            }, cancellationToken);

            var jsonBody = JsonSerializer.Serialize(new
            {
                model = typed.Model,
                prompt = typed.Prompt,
                height = typed.Height,
                width = typed.Width,
                seconds = typed.Seconds,
                fps = typed.Fps,
                seed = typed.Seed,
                output_format = "MP4",
                frame_images = items.Select(a => new { input_image = a }),
                reference_images = referenceImages,
                negative_prompt = typed.NegativePrompt
            });

            using var client = serviceProvider.CreateTogetherClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.together.xyz/v2/videos")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, MimeTypes.Json)
            };

            using var resp = await client.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {json}");

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString();

            if (!typed.WaitUntilCompleted)
            {
                return json.ToJsonCallToolResponse($"https://api.together.xyz/v2/videos/{id}");
            }

            return await DownloadAndUploadTogetherVideoAsync(id!, serviceProvider, requestContext, $"{typed.Filename}.mp4",
                cancellationToken);
        });

    /*   [Description("Fetch metadata and output URL for a generated Together AI video.")]
       [McpServerTool(Title = "Get Together AI Video", Name = "together_video_get", ReadOnly = true)]
       public static async Task<CallToolResult?> TogetherVideo_Get(
           [Description("The ID of the video job to fetch.")] string id,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
           {
               using var client = serviceProvider.CreateTogetherClient();

               using var resp = await client.GetAsync($"https://api.together.xyz/v2/videos/{id}", cancellationToken);
               var json = await resp.Content.ReadAsStringAsync(cancellationToken);
               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {json}");

               return await JsonNode.ParseAsync(BinaryData.FromString(json).ToStream());
           }));*/

    [Description("Please fill in the Together AI video generation request details.")]
    public class TogetherNewVideo
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Video model to use, e.g. black-forest-labs/FLUX.1-schnell-video.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("waitUntilCompleted")]
        [Required]
        [Description("Wait until completed.")]
        public bool WaitUntilCompleted { get; set; } = true;

        [JsonPropertyName("seconds")]
        [Description("Video duration in seconds.")]
        public int? Seconds { get; set; }

        [JsonPropertyName("fps")]
        [Description("Frames per second.")]
        public int? Fps { get; set; }

        [JsonPropertyName("width")]
        [Description("Width of the video in pixels.")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        [Description("Height of the video in pixels.")]
        public int? Height { get; set; }

        [JsonPropertyName("negative_prompt")]
        [Description("Optional negative prompt to exclude elements from generation.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("seed")]
        [Description("Optional random seed for deterministic generation.")]
        public int? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Filename (without extension). Defaults to autogenerated name.")]
        public string? Filename { get; set; }
    }

    private static async Task<CallToolResult?> DownloadAndUploadTogetherVideoAsync(
       string videoId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       string? filename = null,
       CancellationToken cancellationToken = default)
    {
        var client = serviceProvider.CreateTogetherClient();

        string? status = null;
        string? videoUrl;

        do
        {
            using var statusResp = await client.GetAsync($"https://api.together.xyz/v2/videos/{videoId}", cancellationToken);
            var statusJson = await statusResp.Content.ReadAsStringAsync(cancellationToken);

            if (!statusResp.IsSuccessStatusCode)
                throw new Exception($"{statusResp.StatusCode}: {statusJson}");

            var doc = JsonDocument.Parse(statusJson);
            var root = doc.RootElement;
            status = root.GetProperty("status").GetString();

            if (status == "completed")
            {
                videoUrl = root
                    .GetProperty("outputs")
                    .GetProperty("video_url")
                    .GetString();

                if (string.IsNullOrWhiteSpace(videoUrl))
                    throw new Exception("No video URL returned from Together API.");

                filename ??= requestContext.ToOutputFileName("mp4");

                using var videoResp = await client.GetAsync(videoUrl, cancellationToken);
                var videoBytes = await videoResp.Content.ReadAsByteArrayAsync(cancellationToken);

                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    filename,
                    BinaryData.FromBytes(videoBytes),
                    cancellationToken);

                return uploaded?.ToResourceLinkCallToolResponse();
            }

            if (status == "failed")
            {
                var error = root.TryGetProperty("error", out var err)
                    ? err.ToString()
                    : "Unknown error.";

                throw new Exception($"Video generation failed: {error}");
            }

            //   attempt++;
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        } while (status != "completed" && status != "failed");

        throw new TimeoutException($"Video generation did not complete.");
    }
}
