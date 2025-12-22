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
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIImageEditService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/stable-image/edit";

    [Description("Erase unwanted objects or areas from an image using Stability AI’s erase service.")]
    [McpServerTool(
        Title = "Erase objects from image",
        Name = "stabilityai_image_edit_erase",
        Destructive = true)]
    public static async Task<CallToolResult?> StabilityAI_ImageEdit_Erase(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the image to edit (SharePoint, OneDrive, etc.)")] string imageUrl,
        [Description("Optional URL of a mask image (black=keep, white=erase). If omitted, the alpha channel of the image is used.")]
        string? maskUrl = null,
        [Description("Optional mask growth value in pixels (0–20). Default: 5")] int? growMask = 5,
        [Description("Output filename, without extension")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloader = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Download base image
            var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
            var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

            // Optional mask
            var maskItems = !string.IsNullOrWhiteSpace(maskUrl)
                ? await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, maskUrl, cancellationToken)
                : null;
            var maskFile = maskItems?.FirstOrDefault();

            // 1) Elicit any additional input
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new StabilityEraseImage
                {
                    GrowMask = growMask ?? 5,
                },
                cancellationToken);

            // 2) Load API key
            var settings = serviceProvider.GetService<StabilityAISettings>()
                ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

            using var client = clientFactory.CreateClient();
            using var form = new MultipartFormDataContent();

            // Required image
            form.Add("image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType));

            // Optional mask
            if (maskFile is not null)
                form.Add("mask".NamedFile(maskFile.Contents.ToArray(), maskFile.Filename ?? "mask.png", maskFile.MimeType));

            // Optional params
            if (typed?.GrowMask is > 0)
                form.Add("grow_mask".NamedField(typed.GrowMask.ToString()));

            form.Add("output_format".NamedField("png"));

            // Sanity check: every part must have a name
            foreach (var part in form)
            {
                var cd = part.Headers.ContentDisposition;
                if (cd?.Name is null)
                    throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
            }

            // Headers
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

            // 3) POST
            using var resp = await client.PostAsync($"{BASE_URL}/erase", form, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

            // 4) Upload result to Graph / storage
            var graphItem = await requestContext.Server.Upload(
                serviceProvider,
                $"{filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()}.png",
                BinaryData.FromBytes(bytesOut),
                cancellationToken) ?? throw new Exception("Image upload failed");
            return graphItem?.ToCallToolResult();
        });

    [Description("Inpaint or modify specific areas of an image using Stability AI’s inpainting model.")]
    [McpServerTool(
          Title = "Inpaint areas in image",
          Name = "stabilityai_image_edit_inpaint",
          Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageEditInpaint(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Prompt text for inpainting. English only.")] string prompt,
          [Description("URL of the base image to edit. Supports SharePoint and OneDrive links.")] string imageUrl,
          [Description("URL of mask image. White = replace/inpaint, black = keep.")] string maskUrl,
          [Description("Output filename, without extension")] string? filename = null,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          {
              var downloader = serviceProvider.GetRequiredService<DownloadService>();
              var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

              // Download input image
              var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
              var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

              // Optional mask
              var maskItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, maskUrl, cancellationToken);
              var maskFile = maskItems?.FirstOrDefault();

              // 1️⃣ Elicit user confirmation / extra params
              var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                  new StabilityInpaintImage
                  {
                      Prompt = prompt,
                      Filename = filename?.ToOutputFileName()
                                 ?? requestContext.ToOutputFileName()
                  },
                  cancellationToken);

              // 2️⃣ API Key
              var settings = serviceProvider.GetService<StabilityAISettings>()
                  ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

              using var client = clientFactory.CreateClient();
              using var form = new MultipartFormDataContent
              {
                  // Required fields
                  "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType),
                  "prompt".NamedField(typed.Prompt),
                  "output_format".NamedField("png")
              };

              // Optional fields
              if (maskFile is not null)
                  form.Add("mask".NamedFile(maskFile.Contents.ToArray(), maskFile.Filename ?? "mask.png", maskFile.MimeType));

              if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                  form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

              if (typed.GrowMask > 0)
                  form.Add("grow_mask".NamedField(typed.GrowMask.ToString()));

              if (typed.Seed > 0)
                  form.Add("seed".NamedField(typed.Seed.ToString()));

              if (typed.StylePreset.HasValue)
                  form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

              // Validate form
              foreach (var part in form)
              {
                  var cd = part.Headers.ContentDisposition;
                  if (cd?.Name is null)
                      throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
              }

              // Headers
              client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
              client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

              // 3️⃣ POST
              using var resp = await client.PostAsync($"{BASE_URL}/inpaint", form, cancellationToken);
              var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

              if (!resp.IsSuccessStatusCode)
                  throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

              // 4️⃣ Upload and return
              var graphItem = await requestContext.Server.Upload(
                  serviceProvider,
                  $"{typed.Filename}.png",
                  BinaryData.FromBytes(bytesOut),
                  cancellationToken) ?? throw new Exception("Image upload failed");

              return graphItem?.ToCallToolResult();
          });

    [Description("Expand an image in any direction using Stability AI’s outpaint model.")]
    [McpServerTool(
           Title = "Outpaint image",
           Name = "stabilityai_image_edit_outpaint",
           Destructive = true)]
    public static async Task<CallToolResult?> StabilityAI_ImageEdit_Outpaint(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image to expand (SharePoint, OneDrive, or public).")] string imageUrl,
           [Description("Optional description for what should appear in expanded areas. English prompts only.")] string? prompt = null,
           [Description("Left")] int? left = null,
           [Description("Right")] int? right = null,
           [Description("Up")] int? up = null,
           [Description("Down")] int? down = null,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // Download image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 1️⃣ Elicit full input
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityOutpaintImage
                   {
                       Prompt = prompt,
                       Left = left ?? 0,
                       Right = right ?? 0,
                       Up = up ?? 0,
                       Down = down ?? 0,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // Must have at least one outpainting direction
               if (typed.Left == 0 && typed.Right == 0 && typed.Up == 0 && typed.Down == 0)
                   throw new ArgumentException("At least one outpainting direction (left, right, up, down) must be greater than 0.");

               // 2️⃣ API Key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required image
                   "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType)
               };

               // Optional params
               if (!string.IsNullOrWhiteSpace(typed.Prompt))
                   form.Add("prompt".NamedField(typed.Prompt));

               if (typed.Left > 0) form.Add("left".NamedField(typed.Left.ToString()));
               if (typed.Right > 0) form.Add("right".NamedField(typed.Right.ToString()));
               if (typed.Up > 0) form.Add("up".NamedField(typed.Up.ToString()));
               if (typed.Down > 0) form.Add("down".NamedField(typed.Down.ToString()));

               if (typed.Creativity is >= 0 and <= 1)
                   form.Add("creativity".NamedField(typed.Creativity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

               if (typed.Seed > 0)
                   form.Add("seed".NamedField(typed.Seed.ToString()));

               if (typed.StylePreset.HasValue)
                   form.Add("style_preset".NamedField(typed.StylePreset.Value.GetEnumMemberValue()));

               form.Add("output_format".NamedField("png"));

               // Validation
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 3️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/outpaint", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 4️⃣ Upload result
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");

               return graphItem?.ToCallToolResult();
           });

    [Description("Automatically find and replace objects in an image using Stability AI’s Search and Replace service.")]
    [McpServerTool(
           Title = "Search and replace objects in image",
           Name = "stabilityai_image_edit_search_replace",
           Destructive = true)]
    public static async Task<CallToolResult?> StabilityAI_ImageEdit_SearchReplace(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image to edit (SharePoint, OneDrive, or public).")] string imageUrl,
           [Description("Object or area to find (e.g., 'a red car', 'a person', etc.).")] string searchPrompt,
           [Description("Description of what should replace it. English prompts only.")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // Download base image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 1️⃣ Elicit optional parameters
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilitySearchReplaceImage
                   {
                       Prompt = prompt,
                       SearchPrompt = searchPrompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 2️⃣ API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent
               {
                   // Required fields
                   "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType),
                   "prompt".NamedField(typed.Prompt),
                   "search_prompt".NamedField(typed.SearchPrompt),
                   "output_format".NamedField("png")
               };

               // Optional params
               if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                   form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

               if (typed.GrowMask.HasValue && typed.GrowMask > 0)
                   form.Add("grow_mask".NamedField(typed.GrowMask.ToString()!));

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

               // Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 3️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/search-and-replace", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 4️⃣ Upload & return
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");
               return graphItem?.ToCallToolResult();
           });


    [Description("Automatically recolor specific objects in an image using Stability AI’s Search and Recolor service.")]
    [McpServerTool(
           Title = "Search and recolor objects in image",
           Name = "stabilityai_image_edit_search_recolor",
           Destructive = true)]
    public static async Task<CallToolResult?> StabilityAI_ImageEdit_SearchRecolor(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image to recolor (SharePoint, OneDrive, or public).")] string imageUrl,
           [Description("Object to find and recolor (e.g., 'the blue shirt').")] string selectPrompt,
           [Description("Describe the new colors or visual changes. English prompts only.")] string prompt,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // Download base image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 1️⃣ Elicit optional parameters
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilitySearchRecolorImage
                   {
                       Prompt = prompt,
                       SelectPrompt = selectPrompt,
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 2️⃣ API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent();

               // Required fields
               form.Add("image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType));
               form.Add("prompt".NamedField(typed.Prompt));
               form.Add("select_prompt".NamedField(typed.SelectPrompt));
               form.Add("output_format".NamedField("png"));

               // Optional params
               if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                   form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

               if (typed.GrowMask.HasValue && typed.GrowMask > 0)
                   form.Add("grow_mask".NamedField(typed.GrowMask.ToString()!));

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

               // Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 3️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/search-and-recolor", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 4️⃣ Upload & return
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");
               return graphItem?.ToCallToolResult();
           });


    [Description("Remove the background from an image using Stability AI’s background removal model.")]
    [McpServerTool(
           Title = "Remove background from image",
           Name = "stabilityai_image_edit_remove_background",
           Destructive = true)]
    public static async Task<CallToolResult?> StabilityAI_ImageEdit_RemoveBackground(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image to process (SharePoint, OneDrive, or public).")] string imageUrl,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // Download input image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 1️⃣ Elicit user input (mainly output format)
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityRemoveBackgroundImage
                   {
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 2️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent();

               // Required image
               form.Add("image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType));

               // Optional format
               form.Add("output_format".NamedField("png"));

               // Sanity check
               foreach (var part in form)
               {
                   var cd = part.Headers.ContentDisposition;
                   if (cd?.Name is null)
                       throw new InvalidOperationException($"Form part missing name. Headers: {part.Headers}");
               }

               // Headers
               client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
               client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

               // 3️⃣ POST request
               using var resp = await client.PostAsync($"{BASE_URL}/remove-background", form, cancellationToken);
               var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

               if (!resp.IsSuccessStatusCode)
                   throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

               // 4️⃣ Upload result
               var graphItem = await requestContext.Server.Upload(
                   serviceProvider,
                   $"{typed.Filename}.png",
                   BinaryData.FromBytes(bytesOut),
                   cancellationToken) ?? throw new Exception("Image upload failed");
               return graphItem?.ToCallToolResult();
           });

    [Description("Please fill in the Stability AI remove background request.")]
    public class StabilityRemoveBackgroundImage
    {
        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI search and recolor request.")]
    public class StabilitySearchRecolorImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe how the selected object should be recolored.")]
        public string Prompt { get; set; } = default!;

        [Required]
        [JsonPropertyName("select_prompt")]
        [Description("Describe the object to recolor in the image.")]
        public string SelectPrompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what NOT to see in the recolored result.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("grow_mask")]
        [Range(0, 20)]
        [Description("Expands the auto-mask area in pixels (0–20).")]
        public int? GrowMask { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("style_preset")]
        [Description("Optional visual style (cinematic, anime, fantasy-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename, without extension.")]
        public string Filename { get; set; } = default!;
    }

    [Description("Please fill in the Stability AI search and replace request.")]
    public class StabilitySearchReplaceImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe what should replace the detected object.")]
        public string Prompt { get; set; } = default!;

        [Required]
        [JsonPropertyName("search_prompt")]
        [Description("Describe the object or area to find and replace.")]
        public string SearchPrompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the output.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("grow_mask")]
        [Range(0, 20)]
        [Description("Expands the auto-mask area in pixels (0–20).")]
        public int? GrowMask { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("style_preset")]
        [Description("Optional visual style (cinematic, anime, digital-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename, without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI erase request.")]
    public class StabilityEraseImage
    {
        [JsonPropertyName("mask_url")]
        [Description("Optional mask image URL. White = erase, black = keep. If omitted, alpha channel is used.")]
        public string? MaskUrl { get; set; }

        [JsonPropertyName("grow_mask")]
        [Range(0, 20)]
        [Description("Expands the mask area in pixels (0–20). Default: 5.")]
        public int GrowMask { get; set; } = 5;

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 for random).")]
        public uint Seed { get; set; } = 0;

        [JsonPropertyName("filename")]
        [Description("Output file name without extension.")]
        public string Filename { get; set; } = default!;
    }



    [Description("Please fill in the Stability AI inpaint request.")]
    public class StabilityInpaintImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe what should appear in the inpainted area.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what NOT to include in the inpainting result.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("grow_mask")]
        [Range(0, 100)]
        [Description("Expands the mask area outward in pixels (0–100). Default: 5.")]
        public int GrowMask { get; set; } = 5;

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Random seed (0 = random).")]
        public uint Seed { get; set; } = 0;

        [JsonPropertyName("style_preset")]
        [Description("Optional artistic style (anime, cinematic, digital-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output file name, without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI outpaint request.")]
    public class StabilityOutpaintImage
    {
        [JsonPropertyName("prompt")]
        [Description("Describe what should appear in the newly added areas.")]
        public string? Prompt { get; set; }

        [JsonPropertyName("left")]
        [Range(0, 2000)]
        [Description("Number of pixels to outpaint on the left side (0–2000).")]
        public int Left { get; set; } = 0;

        [JsonPropertyName("right")]
        [Range(0, 2000)]
        [Description("Number of pixels to outpaint on the right side (0–2000).")]
        public int Right { get; set; } = 0;

        [JsonPropertyName("up")]
        [Range(0, 2000)]
        [Description("Number of pixels to outpaint on the top side (0–2000).")]
        public int Up { get; set; } = 0;

        [JsonPropertyName("down")]
        [Range(0, 2000)]
        [Description("Number of pixels to outpaint on the bottom side (0–2000).")]
        public int Down { get; set; } = 0;

        [JsonPropertyName("creativity")]
        [Range(0.0, 1.0)]
        [Description("How creative the model should be (0–1). Default: 0.5.")]
        public double Creativity { get; set; } = 0.5;

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint Seed { get; set; } = 0;

        [JsonPropertyName("style_preset")]
        [Description("Optional visual style (e.g., cinematic, fantasy-art, digital-art, etc.).")]
        public StylePreset? StylePreset { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

}
