using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.Runware.Images;

public static class RunwareImages
{
    [Description("Convert a raster image (PNG, JPG, WEBP) into a scalable SVG vector using AI.")]
    [McpServerTool(
         Title = "Runware vectorize image",
         Name = "runware_vectorize",
         OpenWorld = true,
         ReadOnly = false,
         Destructive = false)]
    public static async Task<CallToolResult?> RunwareImages_Vectorize(
         [Description("Input image URL or MCP file URI. SharePoint and OneDrive supported.")] string inputImage,
         [Description("Runware vectorization model (e.g. recraft:1@1 or picsart:1@1).")] string model,
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Output format (default: SVG).")] string outputFormat = "SVG",
         [Description("Include cost info in response.")] bool includeCost = false,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
         {
             // 🧠 1. Elicit parameters with defaults
             var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                 new RunwareVectorize
                 {
                     InputImage = inputImage,
                     Model = model,
                     OutputFormat = outputFormat,
                     IncludeCost = includeCost
                 },
                 cancellationToken);

             var client = serviceProvider.GetRequiredService<RunwareClient>();
             var downloadService = serviceProvider.GetRequiredService<DownloadService>();

             // 🔍 2. Download and encode raster image
             var files = await downloadService.DownloadContentAsync(
                 serviceProvider, requestContext.Server, typed.InputImage, cancellationToken);

             var image = files.FirstOrDefault() ?? throw new Exception("Image not found.");
             var dataUri = image.ToDataUri();

             // 🧱 3. Build task payload
             var task = new
             {
                 taskUUID = Guid.NewGuid().ToString(),
                 taskType = "vectorize",
                 model = typed.Model,
                 outputType = "URL",
                 outputFormat = typed.OutputFormat,
                 includeCost = typed.IncludeCost,
                 inputs = new
                 {
                     image = dataUri
                 }
             };

             var payload = new[] { task };

             // 🚀 4. Send to Runware API
             var resultNode = await client.PostAsync(payload, cancellationToken)
                 ?? throw new Exception("Runware returned no response.");

             var dataArray = resultNode["data"]?.AsArray();
             if (dataArray == null || dataArray.Count == 0)
                 throw new Exception("Runware result contains no data.");
             var uploadedResults = new List<ContentBlock>();

             foreach (var item in dataArray)
             {
                 var imageUrl = item?["imageURL"]?.GetValue<string>();
                 if (string.IsNullOrWhiteSpace(imageUrl))
                     continue;

                 // Download the image
                 var imageBytes = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
                    imageUrl, cancellationToken);

                 if (!imageBytes.Any())
                     continue;

                 // Determine a filename (based on imageUUID or URL)
                 var imageUuid = item?["imageUUID"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                 var fileName = $"{imageUuid}.{typed.OutputFormat}";

                 // Upload to MCP server
                 var uploadResult = await requestContext.Server.Upload(
                   serviceProvider,
                   fileName,
                   imageBytes.First().Contents,
                   cancellationToken);

                 if (uploadResult != null)
                     uploadedResults.Add(uploadResult);
             }

             return new CallToolResult()
             {
                 StructuredContent = resultNode,
                 Content = uploadedResults
             };
         }));

    [Description("Please fill in the Runware vectorization request details.")]
    public class RunwareVectorize
    {
        [JsonPropertyName("inputImage")]
        [Required]
        [Description("URL, MCP file URI, or base64 image to vectorize.")]
        public string InputImage { get; set; } = default!;

        [JsonPropertyName("model")]
        [Required]
        [Description("Runware model identifier (e.g. recraft:1@1 or picsart:1@1).")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("outputFormat")]
        [Description("Output format (default: SVG).")]
        public string OutputFormat { get; set; } = "SVG";

        [JsonPropertyName("includeCost")]
        [Description("Include cost info in response.")]
        public bool IncludeCost { get; set; } = false;
    }

    [Description("Create personalized images using Runware PhotoMaker.")]
    [McpServerTool(
      Title = "Runware PhotoMaker",
      Name = "runware_photomaker",
      OpenWorld = true,
      ReadOnly = false,
      Destructive = false)]
    public static async Task<CallToolResult?> RunwareImages_PhotoMaker(
      [Description("Input image urls. SharePoint and OneDrive links are supported.")] IEnumerable<string> inputImages,
      [Description("Text prompt describing the desired style or scene.")] string positivePrompt,
      [Description("Runware model identifier (SDXL-based).")] string model,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Optional negative prompt to avoid specific elements.")] string? negativePrompt = null,
      [Description("Image style (e.g. No style, Cinematic, Photographic, Digital Art).")] string style = "No style",
      [Description("Transformation strength (15–50, higher = more creative).")] int strength = 15,
      [Description("Image width in pixels (divisible by 64).")] int width = 1024,
      [Description("Image height in pixels (divisible by 64).")] int height = 1024,
      [Description("Number of steps (1–100). Default: 20.")] int steps = 20,
      [Description("Guidance scale (0–50). Default: 7.")] float cfgScale = 7f,
      [Description("Number of images to generate (1–4). Default: 1.")] int numberResults = 1,
      [Description("Include cost info in the response.")] bool includeCost = false,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
      {
          // 🧠 1. Elicit missing parameters with defaults
          var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new RunwarePhotoMaker
              {
                  PositivePrompt = positivePrompt,
                  NegativePrompt = negativePrompt,
                  Model = model,
                  Style = style,
                  Strength = strength,
                  Width = width,
                  Height = height,
                  Steps = steps,
                  CFGScale = cfgScale,
                  NumberResults = numberResults,
                  IncludeCost = includeCost
              },
              cancellationToken);

          var client = serviceProvider.GetRequiredService<RunwareClient>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();

          var base64Images = new List<string>();
          foreach (var uri in inputImages)
          {
              var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, uri, cancellationToken);
              var img = files.FirstOrDefault();
              if (img?.Contents != null)
                  base64Images.Add(img.ToDataUri());
          }

          if (base64Images.Count == 0)
              throw new Exception("No valid reference images found.");

          // 🧱 3. Build payload
          var task = new
          {
              taskUUID = Guid.NewGuid().ToString(),
              taskType = "photoMaker",
              inputImages = base64Images,
              positivePrompt = typed.PositivePrompt,
              negativePrompt = typed.NegativePrompt,
              style = typed.Style,
              strength = typed.Strength,
              model = typed.Model,
              height = typed.Height,
              width = typed.Width,
              steps = typed.Steps,
              typed.CFGScale,
              outputFormat = "JPG",
              numberResults = typed.NumberResults,
              includeCost = typed.IncludeCost
          };

          var payload = new[] { task };

          // 🚀 4. Send to Runware PhotoMaker API
          var resultNode = await client.PostAsync(payload, cancellationToken)
              ?? throw new Exception("Runware returned no response.");

          return resultNode;
      }));

    [Description("Please fill in the Runware PhotoMaker image personalization request details.")]
    public class RunwarePhotoMaker
    {
        [JsonPropertyName("positivePrompt")]
        [Required]
        [Description("Prompt describing the desired look or scene.")]
        public string PositivePrompt { get; set; } = default!;

        [JsonPropertyName("negativePrompt")]
        [Description("Prompt describing what to avoid.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Runware model identifier (must be SDXL-based).")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("style")]
        [Description("Art style (No style, Cinematic, Photographic, etc.).")]
        public string Style { get; set; } = "No style";

        [JsonPropertyName("strength")]
        [Range(15, 50)]
        [Description("Balance between realism and creativity (15–50).")]
        public int Strength { get; set; } = 15;

        [JsonPropertyName("width")]
        [Range(128, 2048)]
        [Description("Width in pixels (divisible by 64).")]
        public int Width { get; set; } = 1024;

        [JsonPropertyName("height")]
        [Range(128, 2048)]
        [Description("Height in pixels (divisible by 64).")]
        public int Height { get; set; } = 1024;

        [JsonPropertyName("steps")]
        [Range(1, 100)]
        [Description("Number of steps (default: 20).")]
        public int Steps { get; set; } = 20;

        [JsonPropertyName("CFGScale")]
        [Range(0, 50)]
        [Description("Guidance scale (higher = closer to prompt).")]
        public float CFGScale { get; set; } = 7f;

        [JsonPropertyName("numberResults")]
        [Range(1, 4)]
        [Description("How many images to generate (default: 1).")]
        public int NumberResults { get; set; } = 1;

        [JsonPropertyName("includeCost")]
        [Description("Include cost info in response.")]
        public bool IncludeCost { get; set; } = false;
    }


    [Description("Preprocess an image using Runware ControlNet (e.g. canny, depth, openpose).")]
    [McpServerTool(
       Title = "Runware ControlNet Preprocess",
       Name = "runware_controlnet_preprocess",
       OpenWorld = true,
       ReadOnly = false,
       Destructive = false)]
    public static async Task<CallToolResult?> RunwareImages_ControlNetPreprocess(
       [Description("Input image url. SharePoint and OneDrive links are supported")] string inputImage,
       [Description("ControlNet preprocessor type (e.g. canny, depth, openpose).")]
       [Required] string preProcessorType,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Image height in pixels (optional).")] int? height = null,
       [Description("Image width in pixels (optional).")] int? width = null,
       [Description("Low threshold for Canny (0–255).")] int? lowThresholdCanny = 100,
       [Description("High threshold for Canny (0–255).")] int? highThresholdCanny = 200,
       [Description("Include hands and face when using OpenPose.")] bool includeHandsAndFaceOpenPose = false,
       [Description("Output format (JPG, PNG, WEBP). Default: JPG.")] string outputFormat = "JPG",
       [Description("Output quality (20–99). Default: 95.")] int outputQuality = 95,
       [Description("Include cost info in response.")] bool includeCost = false,
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
       {
           // 🧠 1. Elicit missing parameters with defaults
           var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
               new RunwareControlNetPreprocess
               {
                   PreProcessorType = preProcessorType,
                   Height = height,
                   Width = width,
                   LowThresholdCanny = lowThresholdCanny ?? 100,
                   HighThresholdCanny = highThresholdCanny ?? 200,
                   IncludeHandsAndFaceOpenPose = includeHandsAndFaceOpenPose,
                   OutputFormat = outputFormat,
                   OutputQuality = outputQuality,
                   IncludeCost = includeCost
               },
               cancellationToken);

           var client = serviceProvider.GetRequiredService<RunwareClient>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           var files = await downloadService.DownloadContentAsync(
               serviceProvider, requestContext.Server, inputImage, cancellationToken);

           var image = files.FirstOrDefault() ?? throw new Exception("File not found");

           var task = new
           {
               taskUUID = Guid.NewGuid().ToString(),
               taskType = "controlNetPreprocess",
               inputImage = image.ToDataUri(),
               preProcessorType = typed.PreProcessorType,
               height = typed.Height,
               width = typed.Width,
               lowThresholdCanny = typed.LowThresholdCanny,
               highThresholdCanny = typed.HighThresholdCanny,
               includeHandsAndFaceOpenPose = typed.IncludeHandsAndFaceOpenPose,
               outputFormat = typed.OutputFormat,
               outputQuality = typed.OutputQuality,
               includeCost = typed.IncludeCost
           };

           var payload = new[] { task };

           // 🚀 4. Send to Runware API
           var resultNode = await client.PostAsync(payload, cancellationToken)
               ?? throw new Exception("Runware returned no response.");

           return resultNode;
       }));

    [Description("Please fill in the Runware ControlNet preprocessing request details.")]
    public class RunwareControlNetPreprocess
    {
        [JsonPropertyName("preProcessorType")]
        [Required]
        [Description("Preprocessor type (e.g. canny, depth, openpose, lineart, seg).")]
        public string PreProcessorType { get; set; } = "canny";

        [JsonPropertyName("height")]
        [Range(64, 2048)]
        [Description("Image height (optional). Maintains aspect ratio if width not set.")]
        public int? Height { get; set; }

        [JsonPropertyName("width")]
        [Range(64, 2048)]
        [Description("Image width (optional). Maintains aspect ratio if height not set.")]
        public int? Width { get; set; }

        [JsonPropertyName("lowThresholdCanny")]
        [Range(0, 255)]
        [Description("Lower Canny threshold (default: 100).")]
        public int LowThresholdCanny { get; set; } = 100;

        [JsonPropertyName("highThresholdCanny")]
        [Range(0, 255)]
        [Description("Higher Canny threshold (default: 200).")]
        public int HighThresholdCanny { get; set; } = 200;

        [JsonPropertyName("includeHandsAndFaceOpenPose")]
        [Description("Include hands/face outlines when using OpenPose.")]
        public bool IncludeHandsAndFaceOpenPose { get; set; } = false;

        [JsonPropertyName("outputFormat")]
        [Description("Output format (JPG, PNG, WEBP). Default: JPG.")]
        public string OutputFormat { get; set; } = "JPG";

        [JsonPropertyName("outputQuality")]
        [Range(20, 99)]
        [Description("Output quality (20–99). Default: 95.")]
        public int OutputQuality { get; set; } = 95;

        [JsonPropertyName("includeCost")]
        [Description("Include cost info in the response.")]
        public bool IncludeCost { get; set; } = false;
    }

    [Description("Create an image using Runware models.")]
    [McpServerTool(
           Title = "Create Runware image",
           Name = "runware_image_create",
           OpenWorld = true,
           ReadOnly = false,
           Destructive = false)]
    public static async Task<CallToolResult?> RunwareImages_Create(
           [Description("Prompt describing the image to generate.")] string prompt,
           [Description("Runware model identifier, e.g. runware:101@1.")] string model,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Seed image url for image-to-image. SharePoint and OneDrive links are supported")] string? seedImage = null,
           [Description("Negative prompt to avoid specific elements.")] string? negativePrompt = null,
           [Description("Image width in pixels.")] int? width = 1024,
           [Description("Image height in pixels.")] int? height = 1024,
           [Description("Output format (JPG, PNG, WEBP).")] string outputFormat = "JPG",
           [Description("Number of inference steps.")] int? steps = 30,
           [Description("Guidance scale (0-50).")] float? cfgScale = 7.5f,
           [Description("Number of images to generate (1-4).")] int? numberResults = 1,
           [Description("Optional seed for reproducibility.")] int? seed = null,
           [Description("Filename (without extension). Defaults to autogenerated name.")] string? filename = null,
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
           //await requestContext.WithStructuredContent(async () =>
               {
                   // 🧠 1. Elicit missing parameters with defaults
                   var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                       new RunwareNewImage
                       {
                           PositivePrompt = prompt,
                           NegativePrompt = negativePrompt,
                           Model = model,
                           Width = width ?? 1024,
                           Height = height ?? 1024,
                           OutputFormat = outputFormat,
                           Steps = steps ?? 30,
                           CFGScale = cfgScale ?? 7.5f,
                           NumberResults = numberResults ?? 1,
                           Seed = seed,
                           Filename = filename
                       },
                       cancellationToken);

                   var client = serviceProvider.GetRequiredService<RunwareClient>();
                   var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                   typed.Filename ??= requestContext.ToOutputFileName("jpg");

                   string? seedImageB64 = null;
                   if (seedImage != null)
                   {
                       var files = await downloadService.DownloadContentAsync(
                           serviceProvider, requestContext.Server, seedImage, cancellationToken);

                       var image = files.First() ?? throw new Exception("File not found");

                       var base64 = Convert.ToBase64String(image.Contents);
                       seedImageB64 = $"data:{image.MimeType};base64,{base64}";
                   }

                   // 🧱 2. Build task payload
                   var task = new
                   {
                       taskUUID = Guid.NewGuid().ToString(),
                       taskType = "imageInference",
                       outputFormat = typed.OutputFormat,
                       positivePrompt = typed.PositivePrompt,
                       negativePrompt = typed.NegativePrompt,
                       height = typed.Height,
                       width = typed.Width,
                       model = typed.Model,
                       seedImage = seedImageB64,
                       steps = typed.Steps,
                       typed.CFGScale,
                       numberResults = typed.NumberResults,
                       seed = typed.Seed
                   };

                   var payload = new[] { task };

                   // 🚀 3. Call Runware
                   var resultNode = await client.PostAsync(payload, cancellationToken)
                       ?? throw new Exception("Runware returned no response.");


                   var dataArray = resultNode["data"]?.AsArray();
                   if (dataArray == null || dataArray.Count == 0)
                       throw new Exception("Runware result contains no data.");
                   var uploadedResults = new List<ContentBlock>();

                   foreach (var item in dataArray)
                   {
                       var imageUrl = item?["imageURL"]?.GetValue<string>();
                       if (string.IsNullOrWhiteSpace(imageUrl))
                           continue;

                       // Download the image
                       var imageBytes = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
                         imageUrl, cancellationToken);

                       if (!imageBytes.Any())
                           continue;

                       // Determine a filename (based on imageUUID or URL)
                       var imageUuid = item?["imageUUID"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                       var fileName = $"{imageUuid}.{typed.OutputFormat}";

                       // Upload to MCP server
                       var uploadResult = await requestContext.Server.Upload(
                           serviceProvider,
                           fileName,
                           imageBytes.First().Contents,
                           cancellationToken);

                       if (uploadResult != null)
                           uploadedResults.Add(uploadResult);
                   }

                   return new CallToolResult()
                   {
                       StructuredContent = resultNode,
                       Content = uploadedResults
                   };
               });

    [Description("Please fill in the Runware image generation request details.")]
    public class RunwareNewImage
    {
        [JsonPropertyName("positivePrompt")]
        [Required]
        [Description("Text prompt describing what to generate.")]
        public string PositivePrompt { get; set; } = default!;

        [JsonPropertyName("negativePrompt")]
        [Description("Prompt describing what to avoid.")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("model")]
        [Required]
        [Description("Runware model identifier, e.g. runware:101@1.")]
        public string Model { get; set; } = default!;

        [JsonPropertyName("width")]
        [Range(128, 2048)]
        [Description("Image width in pixels (divisible by 64).")]
        public int Width { get; set; } = 1024;

        [JsonPropertyName("height")]
        [Range(128, 2048)]
        [Description("Image height in pixels (divisible by 64).")]
        public int Height { get; set; } = 1024;

        [JsonPropertyName("outputFormat")]
        [Description("Output format (JPG, PNG, WEBP). Defaults to JPG.")]
        public string OutputFormat { get; set; } = "JPG";

        [JsonPropertyName("steps")]
        [Range(1, 100)]
        [Description("Number of diffusion steps (default: 30).")]
        public int Steps { get; set; } = 30;

        [JsonPropertyName("CFGScale")]
        [Range(0, 50)]
        [Description("Guidance scale, higher = closer to prompt.")]
        public float CFGScale { get; set; } = 7.5f;

        [JsonPropertyName("numberResults")]
        [Range(1, 4)]
        [Description("How many images to generate (default: 1).")]
        public int NumberResults { get; set; } = 1;

        [JsonPropertyName("seed")]
        [Description("Optional seed for reproducibility.")]
        public int? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Filename (without extension). Defaults to autogenerated name.")]
        public string? Filename { get; set; }
    }
}