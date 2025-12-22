using System.ClientModel;
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
using OpenAI;

namespace MCPhappey.Tools.OpenAI.Video;

public static class OpenAIVideo
{
    [Description("Create a video with OpenAI video generator")]
    [McpServerTool(Title = "Generate video with OpenAI", Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAIVideo_Create(
     [Description("The video prompt.")] string prompt,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Video generation model to use. for example sora-2.")] string model = "sora-2",
     [Description("Clip duration in seconds.")] int seconds = 4,
     [Description("Output resolution formatted as width x height. Supported values are: '720x1280', '1280x720', '1024x1792', and '1792x1024'.")] string size = "720x1280",
     CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();

        var imageInput = new OpenAINewVideo
        {
            Prompt = prompt,
            Model = model,
            Seconds = seconds,
            Size = size
        };

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(imageInput, cancellationToken);

        var dssds = typed.Model;
        var form = new MultipartFormDataContent
            {
                { new StringContent(typed.Model), "model" },
                { new StringContent(typed.Prompt), "prompt" },
                { new StringContent(typed.Seconds.ToString()), "seconds" },
                { new StringContent(typed.Size), "size" }
            };

        // Copy to stream
        var stream = new MemoryStream();
        await form.CopyToAsync(stream);
        stream.Position = 0;

        // Build BinaryContent with correct content type (include boundary!)
        var contentType = "multipart/form-data; boundary=" + form.Headers.ContentType!.Parameters!.First(p => p.Name == "boundary").Value;
        var content = BinaryContent.Create(stream);

        var resultImage = await openAiClient
            .GetVideoClient()
            .CreateVideoAsync(content, contentType);

        return JsonNode.Parse(resultImage.GetRawResponse().Content.ToString());
    }));

    [Description("Retrieve a generated OpenAI video, upload it to OneDrive and delete the remote copy.")]
    [McpServerTool(Title = "Retrieve and upload OpenAI video", Destructive = true, ReadOnly = false)]
    public static async Task<CallToolResult?> OpenAIVideo_Retrieve(
       [Description("The OpenAI video job ID (e.g. video_123).")] string videoId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Optional filename (without extension) for OneDrive upload. Defaults to the video ID.")] string? filename = null,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // STEP 1 — Download video bytes from OpenAI
        var videoClient = openAiClient.GetVideoClient();

        // Use the official SDK wrapper call
        var response = await videoClient.DownloadVideoAsync(videoId);
        var stream = response.GetRawResponse().ContentStream; // If the SDK gives a wrapper
        if (stream == null)
        {
            // fallback to raw content
            stream = response.GetRawResponse().ContentStream
                ?? throw new Exception("No content stream returned from OpenAI.");
        }

        // STEP 2 — Upload to OneDrive via your MCP server
        var fileNameFinal = $"{(string.IsNullOrWhiteSpace(filename) ? videoId : filename)}.mp4";
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            fileNameFinal,
            BinaryData.FromBytes(bytes),
            cancellationToken);

        await videoClient.DeleteVideoAsync(videoId);

        // STEP 4 — Return uploaded link
        return uploaded?.ToResourceLinkCallToolResponse();
    });

    [Description("Please fill in the AI video request details.")]
    public class OpenAINewVideo
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Text prompt that describes the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("The video generation model to use.")]
        public string Model { get; set; } = "sora-2";

        [JsonPropertyName("seconds")]
        [Required]
        [Description("Clip duration in seconds.")]
        public int Seconds { get; set; } = 4;

        [JsonPropertyName("size")]
        [Required]
        [Description("Output resolution formatted as width x height.")]
        public string Size { get; set; } = "720x1280";

    }
}

