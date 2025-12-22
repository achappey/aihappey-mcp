using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Services;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace MCPhappey.Tools.EdenAI.Video;

public static class EdenAIVideo
{
    [Description("Ask a question about a video using Eden AI multimodal models.")]
    [McpServerTool(
        Title = "Video question Answer",
        Name = "edenai_video_question_answer",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_VideoQuestionAnswerAsync(
        [Description("Question about the video content.")] string question,
        [Description("File URL or SharePoint/OneDrive reference to analyze.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Primary provider, e.g. 'google' or 'amazon'.")] string provider = "google",
        [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
        [Description("Temperature between 0 and 1 (controls randomness).")] double temperature = 0,
        [Description("Maximum tokens to generate (1–3000000).")] int maxTokens = 1000,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
        {
            // 1️⃣ Dependencies
            var eden = serviceProvider.GetRequiredService<EdenAIClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            // 2️⃣ Download video
            var files = await downloadService.DownloadContentAsync(
                serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files.FirstOrDefault()
                       ?? throw new Exception("No file found for video question answering input.");

            // 3️⃣ Build multipart/form-data
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(file.Contents.ToArray());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", file.Filename!);

            form.Add(new StringContent(provider), "providers");
            form.Add(new StringContent(question), "text");
            form.Add(new StringContent(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)), "temperature");
            form.Add(new StringContent(maxTokens.ToString()), "max_tokens");
            form.Add(new StringContent("true"), "response_as_dict");
            form.Add(new StringContent("false"), "show_original_response");

            if (!string.IsNullOrWhiteSpace(fallbackProviders))
                form.Add(new StringContent(fallbackProviders), "fallback_providers");

            // 4️⃣ Direct EdenAI request
            using var req = new HttpRequestMessage(HttpMethod.Post, "video/question_answer/")
            {
                Content = form
            };

            // 5️⃣ Send and return structured result
            return await eden.SendAsync(req, cancellationToken);
        }));

    [Description("Retrieve a completed Eden AI video job, upload videos to OneDrive, and delete the job.")]
    [McpServerTool(
      Title = "Retrieve and upload Eden AI video",
      Name = "edenai_video_retrieve",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = true)]
    public static async Task<CallToolResult?> EdenAI_VideoRetrieveAsync(
      [Description("The Eden AI video job ID (public_id).")] string publicId,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Optional filename prefix for OneDrive uploads. Defaults to the job ID.")] string? filenamePrefix = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      {
          // 🧱 1. Dependencies
          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();
          var filePrefix = string.IsNullOrWhiteSpace(filenamePrefix) ? publicId : filenamePrefix;

          // 🔍 2. Get job results
          using var req = new HttpRequestMessage(HttpMethod.Get, $"video/generation_async/{publicId}/");
          var resultNode = await eden.SendAsync(req, cancellationToken)
              ?? throw new Exception("Eden AI returned no response for video job result.");

          var uploaded = new List<ContentBlock>();
          var resultsNode = resultNode["results"];

          if (resultsNode == null)
              throw new Exception("Eden AI response does not contain results.");

          // 🎬 3. Extract and download video URLs
          foreach (var providerResult in resultsNode.AsObject())
          {
              var provider = providerResult.Key;
              var data = providerResult.Value;

              // Each provider result may have a "video_url" or similar
              var videoUrl = data?["video_resource_url"]?.GetValue<string>();

              if (videoUrl == null)
                  continue;

              var files = await downloadService.DownloadContentAsync(
                    serviceProvider, requestContext.Server, videoUrl.Trim(), cancellationToken);

              var file = files.FirstOrDefault();
              if (file == null)
                  continue;

              var fileName = $"{filePrefix}_{provider}_{Guid.NewGuid():N}.mp4";
              var uploadResult = await requestContext.Server.Upload(
                    serviceProvider,
                    fileName,
                    file.Contents,
                    cancellationToken);

              if (uploadResult != null)
                  uploaded.Add(uploadResult);
          }

          // 🧹 4. Delete the job after upload
          using var delReq = new HttpRequestMessage(HttpMethod.Delete, "video/generation_async/");
          await eden.SendAsync(delReq, cancellationToken);

          // 🎯 5. Return structured result + OneDrive uploads
          return new CallToolResult
          {
              StructuredContent = resultNode,
              Content = uploaded
          };
      });


    [Description("Create a new video using Eden AI models.")]
    [McpServerTool(
          Title = "Create Eden AI Video",
          Name = "edenai_video_create",
          OpenWorld = true,
          ReadOnly = false,
          Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_VideoCreate(
          [Description("Prompt describing the video to generate.")] string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Eden provider, e.g. 'google', 'minimax', or 'bytedance'.")] string provider = "google",
          [Description("Video duration in seconds.")] int? duration = null,
          [Description("Frames per second.")] int? fps = null,
          [Description("Video resolution, e.g. '1280x720'.")] string? dimension = null,
          [Description("Optional keyframe image (SharePoint or OneDrive URL).")] string? seedImage = null,
          [Description("Random seed for reproducibility.")] int? seed = null,
          [Description("Filename (without extension). Defaults to autogenerated name.")] string? filename = null,
          CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
              await requestContext.WithStructuredContent(async () =>
              {
                  // 🧠 1. Elicit structured defaults if not all provided
                  var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                      new EdenAIVideoRequest
                      {
                          Prompt = prompt,
                          Provider = provider,
                          Duration = duration ?? 6,
                          Fps = fps ?? 24,
                          Dimension = dimension ?? "1280x720",
                          Seed = seed ?? 12,
                          Filename = filename
                      },
                      cancellationToken);

                  // 🧱 2. Dependencies
                  var eden = serviceProvider.GetRequiredService<EdenAIClient>();
                  var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                  typed.Filename ??= requestContext.ToOutputFileName("mp4");

                  // 🖼️ 3. Optional keyframe image
                  ByteArrayContent? seedImageContent = null;
                  string? seedFileName = null;

                  if (!string.IsNullOrWhiteSpace(seedImage))
                  {
                      var files = await downloadService.DownloadContentAsync(
                          serviceProvider, requestContext.Server, seedImage, cancellationToken);

                      var file = files.FirstOrDefault();
                      if (file != null)
                      {
                          seedImageContent = new ByteArrayContent(file.Contents.ToArray());
                          seedImageContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                          seedFileName = file.Filename!;
                      }
                  }

                  // 🧾 4. Build multipart form
                  using var form = new MultipartFormDataContent
                  {
                      { new StringContent(typed.Provider), "providers" },
                      { new StringContent(typed.Prompt), "text" },
                      { new StringContent(typed.Duration.ToString()), "duration" },
                      { new StringContent(typed.Fps.ToString()), "fps" },
                      { new StringContent(typed.Dimension), "dimension" },
                      { new StringContent(typed.Seed.ToString()), "seed" },
                      { new StringContent("false"), "show_original_response" }
                  };

                  if (seedImageContent != null && seedFileName != null)
                      form.Add(seedImageContent, "file", seedFileName);

                  // 🚀 5. Execute call
                  using var req = new HttpRequestMessage(HttpMethod.Post, "video/generation_async/")
                  {
                      Content = form
                  };

                  return await eden.SendAsync(req, cancellationToken)
                      ?? throw new Exception("Eden AI returned no response.");
              }));

    [Description("Please fill in the Eden AI video generation request details.")]
    public class EdenAIVideoRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Prompt describing the video to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("provider")]
        [Required]
        [Description("Eden provider, e.g. 'google', 'minimax', or 'bytedance'.")]
        public string Provider { get; set; } = "google";

        [JsonPropertyName("duration")]
        [Range(1, 60)]
        [Description("Video duration in seconds. Defaults to 6.")]
        public int Duration { get; set; } = 6;

        [JsonPropertyName("fps")]
        [Range(1, 60)]
        [Description("Frames per second (default 24).")]
        public int Fps { get; set; } = 24;

        [JsonPropertyName("dimension")]
        [Description("Video resolution, e.g. '1280x720'.")]
        public string Dimension { get; set; } = "1280x720";

        [JsonPropertyName("seed")]
        [Description("Random seed for reproducibility (default 12).")]
        public int Seed { get; set; } = 12;

        [JsonPropertyName("filename")]
        [Description("Filename without extension. Defaults to autogenerated name.")]
        public string? Filename { get; set; }
    }
}