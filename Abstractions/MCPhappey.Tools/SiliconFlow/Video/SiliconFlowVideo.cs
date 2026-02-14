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

namespace MCPhappey.Tools.SiliconFlow.Video;

public static class SiliconFlowVideo
{
    [Description("Create a SiliconFlow video, wait for completion, upload the result, and return a resource link block.")]
    [McpServerTool(Title = "Create SiliconFlow video", Name = "siliconflow_video_create", Destructive = false, ReadOnly = false, OpenWorld = true)]
    public static async Task<CallToolResult?> SiliconFlowVideo_Create(
        [Description("Prompt describing the video to generate.")] string prompt,
        [Description("SiliconFlow model name. T2V examples: Wan-AI/Wan2.2-T2V-A14B, Wan-AI/Wan2.1-T2V-14B-720P. I2V examples: Wan-AI/Wan2.2-I2V-A14B, Wan-AI/Wan2.1-I2V-14B-720P.")]
        string model,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Output size. Supported values: 1280x720, 720x1280, 960x960.")] string imageSize = "1280x720",
        [Description("Optional negative prompt.")] string? negativePrompt = null,
        [Description("Optional input image URL for image-to-video models. SharePoint and OneDrive links are supported.")] string? imageUrl = null,
        [Description("Optional random seed.")] int? seed = null,
        [Description("Optional output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var request = new SiliconFlowNewVideo
            {
                Prompt = prompt,
                Model = model,
                ImageSize = imageSize,
                NegativePrompt = negativePrompt,
                ImageUrl = imageUrl,
                Seed = seed,
                Filename = filename
            };

            var (typed, _, _) = await requestContext.Server.TryElicit(request, cancellationToken);

            ValidateImageSize(typed.ImageSize);

            var isI2VModel = typed.Model.Contains("-I2V-", StringComparison.OrdinalIgnoreCase);
            var isT2VModel = typed.Model.Contains("-T2V-", StringComparison.OrdinalIgnoreCase);

            if (isI2VModel && string.IsNullOrWhiteSpace(typed.ImageUrl))
                throw new Exception("This model requires imageUrl (I2V model selected).");

            if (isT2VModel && !string.IsNullOrWhiteSpace(typed.ImageUrl))
                throw new Exception("imageUrl was provided with a T2V model. Use an I2V model when providing imageUrl.");

            var siliconFlow = serviceProvider.GetRequiredService<SiliconFlowClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            string? imageDataUri = null;
            if (!string.IsNullOrWhiteSpace(typed.ImageUrl))
            {
                var imageFiles = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    typed.ImageUrl,
                    cancellationToken);

                var imageFile = imageFiles.FirstOrDefault() ?? throw new Exception("Failed to download input image from imageUrl.");
                var imageMime = string.IsNullOrWhiteSpace(imageFile.MimeType) ? "image/png" : imageFile.MimeType;
                var imageBase64 = Convert.ToBase64String(imageFile.Contents.ToArray());
                imageDataUri = $"data:{imageMime};base64,{imageBase64}";
            }

            var submitBody = new JsonObject
            {
                ["model"] = typed.Model,
                ["prompt"] = typed.Prompt,
                ["image_size"] = typed.ImageSize
            };

            if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                submitBody["negative_prompt"] = typed.NegativePrompt;

            if (typed.Seed.HasValue)
                submitBody["seed"] = typed.Seed.Value;

            if (!string.IsNullOrWhiteSpace(imageDataUri))
                submitBody["image"] = imageDataUri;

            var submitResponse = await siliconFlow.PostJsonAsync("video/submit", submitBody, cancellationToken);
            var requestId = submitResponse?["requestId"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(requestId))
                throw new Exception("SiliconFlow did not return requestId.");

            while (true)
            {
                var statusResponse = await siliconFlow.PostJsonAsync("video/status", new { requestId }, cancellationToken);
                var status = statusResponse?["status"]?.GetValue<string>() ?? string.Empty;

                if (status.Equals("Succeed", StringComparison.OrdinalIgnoreCase))
                {
                    var videoUrl = statusResponse?["results"]?["videos"]?[0]?["url"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(videoUrl))
                        throw new Exception("SiliconFlow completed without a video URL.");

                    var videos = await downloadService.DownloadContentAsync(
                        serviceProvider,
                        requestContext.Server,
                        videoUrl,
                        cancellationToken);

                    var video = videos.FirstOrDefault() ?? throw new Exception("Failed to download generated video from SiliconFlow result URL.");

                    var uploadFilename = BuildFilename(typed.Filename, requestContext);
                    var uploaded = await requestContext.Server.Upload(
                        serviceProvider,
                        uploadFilename,
                        video.Contents,
                        cancellationToken);

                    return uploaded?.ToResourceLinkCallToolResponse();
                }

                if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = statusResponse?["reason"]?.GetValue<string>() ?? "Unknown failure.";
                    throw new Exception($"SiliconFlow video generation failed. requestId={requestId}. reason={reason}");
                }

                await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            }

        });

    [Description("Please fill in the SiliconFlow video generation request details.")]
    public class SiliconFlowNewVideo
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("SiliconFlow video model.")]
        public string Model { get; set; } = "Wan-AI/Wan2.2-T2V-A14B";

        [JsonPropertyName("image_size")]
        [Required]
        [Description("Output size. Supported values: 1280x720, 720x1280, 960x960.")]
        public string ImageSize { get; set; } = "1280x720";

        [JsonPropertyName("negative_prompt")]
        [Description("Optional negative prompt.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("imageUrl")]
        [Description("Optional input image URL for I2V models. SharePoint and OneDrive links are supported.")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("seed")]
        [Description("Optional random seed.")]
        public int? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Optional output filename without extension.")]
        public string? Filename { get; set; }
    }

    private static void ValidateImageSize(string imageSize)
    {
        if (imageSize is "1280x720" or "720x1280" or "960x960")
            return;

        throw new Exception("Invalid imageSize. Supported values: 1280x720, 720x1280, 960x960.");
    }

    private static string BuildFilename(string? filename, RequestContext<CallToolRequestParams> requestContext)
    {
        var baseName = string.IsNullOrWhiteSpace(filename)
            ? requestContext.ToOutputFileName("mp4")
            : filename.Trim();

        return baseName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"{baseName}.mp4";
    }
}

