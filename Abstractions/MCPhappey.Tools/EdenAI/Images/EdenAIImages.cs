using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Services;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;
using System.Net.Http.Headers;

namespace MCPhappey.Tools.EdenAI.Images;

public static class EdenAIImages
{
    [Description("Ask a question about an image using Eden AI multimodal models.")]
    [McpServerTool(
       Title = "Image question answer",
       Name = "edenai_image_question_answer",
       OpenWorld = true,
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_ImageQuestionAnswerAsync(
       [Description("Question about the image.")] string question,
       [Description("File URL or SharePoint/OneDrive reference to analyze.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Primary provider, e.g. 'openai' or 'google'.")] string provider = "openai",
       [Description("Fallback providers (comma separated).")] string? fallbackProviders = null,
       [Description("Temperature between 0 and 1 (controls randomness).")] double temperature = 0,
       [Description("Maximum tokens to generate (1–2048).")] int maxTokens = 500,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
       {
           // 1️⃣ Dependencies
           var eden = serviceProvider.GetRequiredService<EdenAIClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 2️⃣ Download image
           var files = await downloadService.DownloadContentAsync(
               serviceProvider, requestContext.Server, fileUrl, cancellationToken);
           var file = files.FirstOrDefault()
                      ?? throw new Exception("No file found for image question answering input.");

           // 3️⃣ Build multipart/form-data
           using var form = new MultipartFormDataContent();
           var fileContent = new ByteArrayContent(file.Contents.ToArray());
           fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
           form.Add(fileContent, "file", file.Filename!);

           form.Add(new StringContent(provider), "providers");
           form.Add(new StringContent(question), "question");
           form.Add(new StringContent(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)), "temperature");
           form.Add(new StringContent(maxTokens.ToString()), "max_tokens");
           form.Add(new StringContent("true"), "response_as_dict");
           form.Add(new StringContent("false"), "show_original_response");

           if (!string.IsNullOrWhiteSpace(fallbackProviders))
               form.Add(new StringContent(fallbackProviders), "fallback_providers");

           // 4️⃣ Direct EdenAI request
           using var req = new HttpRequestMessage(HttpMethod.Post, "image/question_answer/")
           {
               Content = form
           };

           // 5️⃣ Send and return structured result
           return await eden.SendAsync(req, cancellationToken);
       }));

    [Description("Detect if an image is AI-generated using Eden AI detection models.")]
    [McpServerTool(
     Title = "Detect AI-generated image",
     Name = "edenai_image_ai_detection",
     OpenWorld = true,
     ReadOnly = false,
     Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_ImageAIDetect(
     [Description("File URL or SharePoint/OneDrive reference to analyze.")] string fileUrl,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Primary provider, e.g. 'winstonai'.")] string provider = "winstonai",
     [Description("Optional fallback providers (comma separated).")] string? fallbackProviders = null,
     CancellationToken cancellationToken = default)
     => await requestContext.WithExceptionCheck(async () =>
     await requestContext.WithStructuredContent(async () =>
     {
         var eden = serviceProvider.GetRequiredService<EdenAIClient>();
         var downloadService = serviceProvider.GetRequiredService<DownloadService>();

         // 2️⃣ Download image file
         var files = await downloadService.DownloadContentAsync(
             serviceProvider, requestContext.Server, fileUrl, cancellationToken);
         var file = files.FirstOrDefault() ?? throw new Exception("No file found for AI detection input.");

         // 3️⃣ Build multipart/form-data
         using var form = new MultipartFormDataContent();
         var fileContent = new ByteArrayContent(file.Contents.ToArray());
         fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
         form.Add(fileContent, "file", file.Filename!);

         form.Add(new StringContent(provider), "providers");
         form.Add(new StringContent("true"), "response_as_dict");
         form.Add(new StringContent("false"), "show_original_response");

         if (!string.IsNullOrWhiteSpace(fallbackProviders))
             form.Add(new StringContent(fallbackProviders), "fallback_providers");

         // 4️⃣ Direct HTTP request (auth handled by EdenAIClient)
         using var req = new HttpRequestMessage(HttpMethod.Post, "image/ai_detection/")
         {
             Content = form
         };

         // 5️⃣ Execute call and return structured content
         return await eden.SendAsync(req, cancellationToken);
     }));


    [Description("Generate an image from text using Eden AI providers.")]
    [McpServerTool(
      Title = "Create Eden AI image",
      Name = "edenai_image_generate",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> EdenAI_ImageGenerate(
      [Description("Prompt describing the image to generate.")] string prompt,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Eden provider, e.g. openai, stabilityai, leonardo.")] string provider = "openai",
      [Description("Resolution, e.g. 512x512 or 1024x1024.")] string resolution = "1024x1024",
      [Description("Number of images to generate (1–10).")] int numberImages = 1,
      [Description("Optional fallback providers (comma separated).")] string? fallbackProviders = null,
      [Description("Optional filename (without extension). Defaults to autogenerated name.")] string? filename = null,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      {
          // 🧠 1. Elicit missing parameters with defaults
          var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new EdenAIImageRequest
              {
                  Prompt = prompt,
                  Provider = provider,
                  Resolution = resolution,
                  NumberImages = numberImages,
                  FallbackProviders = fallbackProviders,
                  Filename = filename
              },
              cancellationToken);

          var eden = serviceProvider.GetRequiredService<EdenAIClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();
          typed.Filename ??= requestContext.ToOutputFileName("png");

          // 🧱 2. Build JSON payload
          var payload = new Dictionary<string, object?>
          {
              ["providers"] = typed.Provider,
              ["text"] = typed.Prompt,
              ["resolution"] = typed.Resolution,
              ["num_images"] = typed.NumberImages,
              ["response_as_dict"] = true,
              ["show_original_response"] = false
          };

          if (!string.IsNullOrWhiteSpace(typed.FallbackProviders))
              payload["fallback_providers"] = typed.FallbackProviders;

          // 🚀 3. Call Eden AI
          var resultNode = await eden.PostAsync("image/generation/", payload, cancellationToken)
              ?? throw new Exception("Eden AI returned no response.");

          // 🧾 4. Extract image URLs
          var providerKey = resultNode.AsObject().First().Key;
          var providerResult = resultNode[providerKey];
          var images = providerResult?["items"] ?? providerResult?["generated_images"] ?? providerResult?["results"];

          var uploadedResults = new List<ContentBlock>();

          if (images != null && images.AsArray().Count > 0)
          {
              foreach (var img in images.AsArray())
              {
                  var url = img?["image_resource_url"]?.GetValue<string>()
                         ?? img?["image_url"]?.GetValue<string>()
                         ?? img?.GetValue<string>();
                  if (string.IsNullOrWhiteSpace(url))
                      continue;

                  var files = await downloadService.DownloadContentAsync(
                      serviceProvider, requestContext.Server, url!, cancellationToken);

                  if (!files.Any())
                      continue;

                  var fileName = typed.Filename!;
                  var uploadResult = await requestContext.Server.Upload(
                      serviceProvider,
                      fileName,
                      files.First().Contents,
                      cancellationToken);

                  if (uploadResult != null)
                      uploadedResults.Add(uploadResult);
              }
          }

          // 🎯 5. Return structured result
          return new CallToolResult()
          {
              StructuredContent = resultNode,
              Content = uploadedResults
          };
      });

    [Description("Please fill in the Eden AI image generation request details.")]
    public class EdenAIImageRequest
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("Text prompt describing what to generate.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("provider")]
        [Required]
        [Description("Eden AI image provider, e.g. openai, stabilityai, leonardo.")]
        public string Provider { get; set; } = "openai";

        [JsonPropertyName("resolution")]
        [Description("Resolution such as 512x512 or 1024x1024.")]
        public string Resolution { get; set; } = "1024x1024";

        [JsonPropertyName("numberImages")]
        [Range(1, 10)]
        [Description("How many images to generate (1–10).")]
        public int NumberImages { get; set; } = 1;

        [JsonPropertyName("fallbackProviders")]
        [Description("Fallback providers if primary fails.")]
        public string? FallbackProviders { get; set; }

        [JsonPropertyName("filename")]
        [Description("Filename without extension (default autogenerated).")]
        public string? Filename { get; set; }
    }

}