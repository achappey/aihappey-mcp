using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Runware.Utilities;

public static class RunwareUtilities
{

    [Description("Generate an AI caption or description for an image using Runware Caption API.")]
    [McpServerTool(
     Title = "Runware Image Caption",
     Name = "runware_caption",
     OpenWorld = true,
     ReadOnly = false,
     Destructive = false)]
    public static async Task<CallToolResult?> RunwareUtilities_Caption(
     [Description("Input image URL or MCP file URI. SharePoint and OneDrive supported.")] string inputImage,
     [Description("Runware model identifier (e.g. runware:150@2).")] string model,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Optional question or instruction for the caption model.")] string? prompt = null,
     [Description("Include cost info in the response.")] bool includeCost = false,
     CancellationToken cancellationToken = default)
     => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
         {
             var client = serviceProvider.GetRequiredService<RunwareClient>();
             var downloadService = serviceProvider.GetRequiredService<DownloadService>();

             // 🔍 2. Download and encode image to base64
             var files = await downloadService.DownloadContentAsync(
                 serviceProvider, requestContext.Server, inputImage, cancellationToken);

             var image = files.FirstOrDefault() ?? throw new Exception("Image not found.");
             var imageDataUri = image.ToDataUri();

             // 🧱 3. Build payload
             var task = new
             {
                 taskUUID = Guid.NewGuid().ToString(),
                 taskType = "caption",
                 inputImage = imageDataUri,
                 model,
                 prompt,
                 includeCost
             };

             var payload = new[] { task };

             // 🚀 4. Send request
             var resultNode = await client.PostAsync(payload, cancellationToken)
                 ?? throw new Exception("Runware returned no response.");

             return resultNode;
         }));


    [Description("Enhance a prompt with additional creative keywords via Runware.")]
    [McpServerTool(
      Title = "Runware Prompt Enhancer",
      Name = "runware_prompt_enhance",
      ReadOnly = false)]
    public static async Task<CallToolResult?> Runware_PromptEnhance(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("The base prompt text to enhance.")]
        string prompt,
      [Description("Maximum token length of the enhanced prompt (12–400). Default 64.")]
        int promptMaxLength = 64,
      [Description("Number of enhanced prompt versions (1–5). Default 4.")]
        int promptVersions = 4,
      [Description("Include cost info in the response.")]
        bool includeCost = false,
      CancellationToken cancellationToken = default)
      => await requestContext!.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
          {
              var client = serviceProvider!.GetRequiredService<RunwareClient>();

              var task = new
              {
                  taskType = "promptEnhance",
                  taskUUID = Guid.NewGuid().ToString(),
                  prompt,
                  promptMaxLength,
                  promptVersions,
                  includeCost
              };

              var payload = new[] { task };

              var resultNode = await client.PostAsync(payload, cancellationToken)
                  ?? throw new Exception("Runware returned no response.");

              return resultNode;
          }));

    [Description("Fetch the latest status or result of a Runware task.")]
    [McpServerTool(
        Title = "Get Runware task response",
        Name = "runware_get_response",
        ReadOnly = true)]
    public static async Task<CallToolResult?> Runware_GetResponse(
        [Description("The UUID of the Runware task to check.")]
        [Required] string taskUUID,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<RunwareClient>();

                // Build the single-task payload
                var payload = new[]
                {
                    new
                    {
                        taskType = "getResponse",
                        taskUUID
                    }
                };

                // Post to Runware and return the raw JSON
                var resultNode = await client.PostAsync(payload, cancellationToken)
                    ?? throw new Exception("Runware returned no response.");

                return resultNode;
            }));

    [Description("Search available models on the Runware platform.")]
    [McpServerTool(
           Title = "Runware model search",
           Name = "runware_model_search",
           ReadOnly = true)]
    public static async Task<CallToolResult?> Runware_ModelSearch(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Free-text search term for model discovery.")] string? search = null,
           [Description("Filter by tags (comma or space separated).")] string? tags = null,
           [Description("Filter by category (e.g., checkpoint, lora, vae).")] string? category = null,
           [Description("Filter by type (base, inpainting, refiner).")] string? type = null,
           [Description("Filter by architecture (e.g., sdxl, flux1s, imagen3).")] string? architecture = null,
           [Description("Filter by visibility (public, private, all).")] string visibility = "all",
           [Range(1, 100)] int limit = 20,
           [Range(0, int.MaxValue)] int offset = 0,
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
               await requestContext.WithStructuredContent(async () =>
           {
               var client = serviceProvider!.GetRequiredService<RunwareClient>();

               var task = new
               {
                   taskUUID = Guid.NewGuid().ToString(),
                   taskType = "modelSearch",
                   search,
                   tags = string.IsNullOrWhiteSpace(tags)
                       ? null
                       : tags.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries),
                   category,
                   type,
                   architecture,
                   visibility,
                   offset,
                   limit
               };

               var payload = new[] { task };

               var result = await client.PostAsync(payload, cancellationToken);

               if (result == null)
                   throw new Exception("Runware returned no data.");

               return result;
           }));
}