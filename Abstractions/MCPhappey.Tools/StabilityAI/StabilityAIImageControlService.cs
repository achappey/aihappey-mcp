using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI.Enums;
using MCPhappey.Tools.StabilityAI.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIImageControlService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/stable-image/control";

    [Description("Refine or reimagine hand-drawn sketches using Stability AI’s Sketch Control model.")]
    [McpServerTool(
           Title = "Sketch-to-Image (Control Sketch) with Stability AI",
           Name = "stabilityai_image_control_sketch",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageControl_Sketch(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the sketch or image to refine. Supports SharePoint and OneDrive.")] string imageUrl,
           [Description("Describe what the final refined image should look like. English prompts only.")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Download input image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 2️⃣ Elicit optional parameters
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityControlSketchImage
                   {
                       Prompt = prompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 3️⃣ API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required
                   "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType),
                   "prompt".NamedField(typed.Prompt),
                   "output_format".NamedField("png")
               };

               // Optional fields
               if (typed.ControlStrength.HasValue)
                   form.Add("control_strength".NamedField(
                       typed.ControlStrength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                   form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

               if (typed.Seed.HasValue && typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()!));

               if (typed.StylePreset.HasValue)
                   form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // 4️⃣ Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 5️⃣ POST
               using var resp = await client.PostAsync($"{BASE_URL}/sketch", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 6️⃣ Upload & return
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");

               return graphItem?.ToCallToolResult();
           });

    [Description("Generate new images that preserve the structure of an input image using Stability AI’s Structure Control model.")]
    [McpServerTool(
          Title = "Structure-based generation (Control Structure) with Stability AI",
          Name = "stabilityai_image_control_structure",
          Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageControl_Structure(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("URL of the image whose structure will guide generation. Supports SharePoint and OneDrive.")] string imageUrl,
          [Description("Describe what the final image should depict while keeping the same structure. English prompts only.")] string prompt,
          [Description("Output filename without extension.")] string? filename = null,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          {
              var downloader = serviceProvider.GetRequiredService<DownloadService>();
              var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

              // 1️⃣ Download image
              var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
              var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

              // 2️⃣ Elicit optional parameters
              var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                  new StabilityControlStructureImage
                  {
                      Prompt = prompt,
                      Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                  },
                  cancellationToken);

              // 3️⃣ API key
              var settings = serviceProvider.GetService<StabilityAISettings>()
                  ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

              using var client = clientFactory.CreateClient();
              using var form = new MultipartFormDataContent
              {
                  // Required
                  "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType),
                  "prompt".NamedField(typed.Prompt),
                  "output_format".NamedField("png")
              };

              // Optional
              if (typed.ControlStrength.HasValue)
                  form.Add("control_strength".NamedField(
                      typed.ControlStrength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

              if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                  form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

              if (typed.Seed.HasValue && typed.Seed > 0)
                  form.Add("seed".NamedField(typed.Seed.ToString()!));

              if (typed.StylePreset.HasValue)
                  form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

              // Sanity check
              foreach (var part in form)
              {
                  var cd = part.Headers.ContentDisposition;
                  if (cd?.Name is null)
                      throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
              }

              // 4️⃣ Headers
              client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
              client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

              // 5️⃣ POST
              using var resp = await client.PostAsync($"{BASE_URL}/structure", form, cancellationToken);
              var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

              if (!resp.IsSuccessStatusCode)
                  throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

              // 6️⃣ Upload & return
              var graphItem = await requestContext.Server.Upload(
                  serviceProvider,
                  $"{typed.Filename}.png",
                  BinaryData.FromBytes(bytesOut),
                  cancellationToken) ?? throw new Exception("Image upload failed");

              return graphItem?.ToCallToolResult();
          });

    [Description("Generate new images guided by the style of a reference image using Stability AI’s Style Guide model.")]
    [McpServerTool(
           Title = "Style transfer generation (Control Style Guide) with Stability AI",
           Name = "stabilityai_image_control_style_guide",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageControl_StyleGuide(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the reference image whose style should guide the output.")] string imageUrl,
           [Description("Describe what the output should depict, in the same style as the reference image. English prompts only.")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Download style reference image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download style reference image");

               // 2️⃣ Elicit optional parameters
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityControlStyleGuideImage
                   {
                       Prompt = prompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 3️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required fields
                   "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "style.png", imageFile.MimeType),
                   "prompt".NamedField(typed.Prompt),
                   "output_format".NamedField("png")
               };

               // Optional params
               if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                   form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

               if (typed.AspectRatio.HasValue)
                   form.Add("aspect_ratio".NamedField(typed.AspectRatio.Value.GetEnumMemberValue()));

               if (typed.Fidelity.HasValue)
                   form.Add("fidelity".NamedField(typed.Fidelity.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.Seed.HasValue && typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()!));

               if (typed.StylePreset.HasValue)
                   form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // 4️⃣ Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 5️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/style-guide", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 6️⃣ Upload & return
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");

               return graphItem?.ToCallToolResult();
           });

    [Description("Transfer visual style from one image to another while preserving composition and structure.")]
    [McpServerTool(
           Title = "Style Transfer (Control Style Transfer) with Stability AI",
           Name = "stabilityai_image_control_style_transfer",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageControl_StyleTransfer(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image whose content should be preserved.")] string initImageUrl,
           [Description("URL of the image whose style should be applied.")] string styleImageUrl,
           [Description("Optional description of the desired final look. English prompts only.")] string? prompt = null,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // 1️⃣ Download both images
               var initFiles = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, initImageUrl, cancellationToken);
               var styleFiles = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, styleImageUrl, cancellationToken);

               var initFile = initFiles.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download init image");
               var styleFile = styleFiles.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download style image");

               // 2️⃣ Elicit user parameters
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityControlStyleTransferImage
                   {
                       Prompt = prompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 3️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required files
                   "init_image".NamedFile(initFile.Contents.ToArray(), initFile.Filename ?? "init.png", initFile.MimeType),
                   "style_image".NamedFile(styleFile.Contents.ToArray(), styleFile.Filename ?? "style.png", styleFile.MimeType)
               };

               // Optional fields
               if (!string.IsNullOrWhiteSpace(typed.Prompt))
                   form.Add("prompt".NamedField(typed.Prompt!));

               if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                   form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

               if (typed.StyleStrength.HasValue)
                   form.Add("style_strength".NamedField(
                       typed.StyleStrength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.CompositionFidelity.HasValue)
                   form.Add("composition_fidelity".NamedField(
                       typed.CompositionFidelity.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.ChangeStrength.HasValue)
                   form.Add("change_strength".NamedField(
                       typed.ChangeStrength.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.Seed.HasValue && typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()!));

               form.Add("output_format".NamedField("png"));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // 4️⃣ Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 5️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/style-transfer", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 6️⃣ Upload result
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");

               // 7️⃣ Return response
               return graphItem?.ToCallToolResult();
           });

    [Description("Please fill in the Stability AI style transfer request.")]
    public class StabilityControlStyleTransferImage
    {

        [JsonPropertyName("prompt")]
        [Description("Describe how the final image should look (optional refinement prompt).")]
        public string? Prompt { get; set; }

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the final image.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("style_strength")]
        [Range(0, 1)]
        [Description("Influence of the style image (0 = minimal, 1 = full style transfer).")]
        public double? StyleStrength { get; set; }

        [JsonPropertyName("composition_fidelity")]
        [Range(0, 1)]
        [Description("How closely the structure/composition of the init image is maintained (0 = loose, 1 = strict).")]
        public double? CompositionFidelity { get; set; }

        [JsonPropertyName("change_strength")]
        [Range(0.1, 1)]
        [Description("How much the original image should change overall. Default: 0.9.")]
        public double? ChangeStrength { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI style guide control request.")]
    public class StabilityControlStyleGuideImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe the new image you want, in the same style as the reference image.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the generated image.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("aspect_ratio")]
        [Description("Aspect ratio of the output image (e.g., 1:1, 16:9, 9:16, etc.).")]
        public AspectRatio? AspectRatio { get; set; }

        [JsonPropertyName("fidelity")]
        [Range(0, 1)]
        [Description("How closely the output follows the style of the input (0 = loose, 1 = very close).")]
        public double? Fidelity { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("style_preset")]
        [Description("Optional target style (cinematic, anime, digital-art, fantasy-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI structure control request.")]
    public class StabilityControlStructureImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe what should be generated while maintaining the original image’s structure.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("control_strength")]
        [Range(0, 1)]
        [Description("Influence of the input structure (0 = loose, 1 = strict). Default: 0.7")]
        public double? ControlStrength { get; set; }

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the output image.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("style_preset")]
        [Description("Optional visual style (e.g., cinematic, digital-art, fantasy-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI sketch control request.")]
    public class StabilityControlSketchImage
    {

        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe what the refined image should look like (colors, shapes, materials, etc.).")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("control_strength")]
        [Range(0, 1)]
        [Description("Influence of the input sketch (0 = loose interpretation, 1 = strict control). Default: 0.7")]
        public double? ControlStrength { get; set; }

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the result.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("style_preset")]
        [Description("Optional visual style (anime, cinematic, digital-art, fantasy-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

}
