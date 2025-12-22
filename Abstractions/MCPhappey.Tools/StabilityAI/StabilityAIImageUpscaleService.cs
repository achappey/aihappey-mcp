using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.StabilityAI.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.StabilityAI;

public static class StabilityAIImageUpscaleService
{
    private const string BASE_URL = "https://api.stability.ai/v2beta/stable-image/upscale";

    [Description("Upscale images up to 4K using Stability AI’s conservative model (minimal alteration, high fidelity).")]
    [McpServerTool(
        Title = "Conservative upscale image",
        Name = "stabilityai_image_upscale_conservative",
        Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageUpscale_Conservative(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the image to upscale (SharePoint, OneDrive, or public).")] string imageUrl,
        [Description("Describe what should stay or be lightly refined in the upscale result. English prompts only.")] string prompt,
        [Description("Output filename without extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var downloader = serviceProvider.GetRequiredService<DownloadService>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Download image
            var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
            var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

            // 1️⃣ Elicit parameters
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new StabilityUpscaleConservativeImage
                {
                    Prompt = prompt,
                    Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                },
                cancellationToken);

            // 2️⃣ API key
            var settings = serviceProvider.GetService<StabilityAISettings>()
                ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

            using var client = clientFactory.CreateClient();
            using var form = new MultipartFormDataContent
            {
                // Required image + prompt
                "image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType),
                "prompt".NamedField(typed.Prompt),
                "output_format".NamedField("png")
            };

            // Optional fields
            if (!string.IsNullOrWhiteSpace(typed.NegativePrompt))
                form.Add("negative_prompt".NamedField(typed.NegativePrompt!));

            if (typed.Seed.HasValue && typed.Seed > 0)
                form.Add("seed".NamedField(typed.Seed.ToString()!));

            if (typed.Creativity.HasValue)
                form.Add("creativity".NamedField(
                    typed.Creativity.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

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

            // 3️⃣ POST
            using var resp = await client.PostAsync($"{BASE_URL}/conservative", form, cancellationToken);
            var bytesOut = await resp.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytesOut)}");

            // 4️⃣ Upload
            var graphItem = await requestContext.Server.Upload(
                serviceProvider,
                $"{typed.Filename}.png",
                BinaryData.FromBytes(bytesOut),
                cancellationToken) ?? throw new Exception("Image upload failed");

            return graphItem?.ToCallToolResult();
        });

    [Description("Enhance image resolution by 4x in ~1 second using Stability AI's Fast Upscaler.")]
    [McpServerTool(
           Title = "Fast upscale image",
           Name = "stabilityai_image_upscale_fast",
           Destructive = false)]
    public static async Task<CallToolResult?> StabilityAI_ImageUpscale_Fast(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("URL of the image to upscale (SharePoint, OneDrive, or public).")] string imageUrl,
           [Description("Output filename without extension.")] string? filename = null,
           CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           {
               var downloader = serviceProvider.GetRequiredService<DownloadService>();
               var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

               // Download input image
               var imageItems = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, imageUrl, cancellationToken);
               var imageFile = imageItems.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download image");

               // 1️⃣ Elicit output format (optional)
               var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                   new StabilityUpscaleFastImage
                   {
                       Filename = filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()
                   },
                   cancellationToken);

               // 2️⃣ Load API key
               var settings = serviceProvider.GetService<StabilityAISettings>()
                   ?? throw new InvalidOperationException("No StabilityAISettings found in service provider");

               using var client = clientFactory.CreateClient();
               using var form = new MultipartFormDataContent();

               // Required
               form.Add("image".NamedFile(imageFile.Contents.ToArray(), imageFile.Filename ?? "image.png", imageFile.MimeType));
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
               using var resp = await client.PostAsync($"{BASE_URL}/fast", form, cancellationToken);
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

    [Description("Please fill in the Stability AI conservative upscale request.")]
    public class StabilityUpscaleConservativeImage
    {
        [Required]
        [JsonPropertyName("prompt")]
        [Description("Describe what should remain or be slightly refined in the upscaled result.")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("negative_prompt")]
        [Description("Describe what should NOT appear in the upscaled image (optional).")]
        public string? NegativePrompt { get; set; }

        [JsonPropertyName("creativity")]
        [Range(0.2, 0.5)]
        [Description("Degree of enhancement freedom (0.2–0.5). Lower = safer, higher = slightly more creative.")]
        public double? Creativity { get; set; }

        [JsonPropertyName("seed")]
        [Range(0, uint.MaxValue - 1)]
        [Description("Randomness seed (0 = random).")]
        public uint? Seed { get; set; }

        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }


    [Description("Please fill in the Stability AI fast upscale request.")]
    public class StabilityUpscaleFastImage
    {
        [JsonPropertyName("filename")]
        [Description("Output filename without extension.")]
        public string Filename { get; set; } = default!;
    }

}
