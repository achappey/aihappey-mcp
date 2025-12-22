using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.xAI;

public static class GrokImageService
{
    [Description("Create an image with xAI Grok image generator")]
    [McpServerTool(Title = "Generate image with xAI Grok", Name = "xai_images_create", Destructive = false)]
    public static async Task<CallToolResult?> XAIImages_Create(
       [Description("Image prompt (English only)")]
        string prompt,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("The number of images to generate (1–10).")]
        [Range(1, 10)] int numberOfImages = 1,
       [Description("New image file name, without extension")]
        string? filename = null,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
   {
       var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
           new GrokNewImage
           {
               Prompt = prompt,
               NumberOfImages = numberOfImages,
               Filename = filename?.ToOutputFileName()
                         ?? requestContext.ToOutputFileName()
           },
           cancellationToken);

       // 1) Build HTTP client
       var settings = serviceProvider.GetService<XAISettings>()
          ?? throw new InvalidOperationException("No XAISettings found in service provider");

       using var client = new HttpClient { BaseAddress = new Uri("https://api.x.ai/") };
       client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

       // 2) Build request payload
       var payload = new
       {
           model = "grok-2-image-latest",
           prompt = typed.Prompt,
           n = typed.NumberOfImages,
           response_format = "b64_json"
       };

       var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
       {
           DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
           PropertyNamingPolicy = JsonNamingPolicy.CamelCase
       });

       using var req = new HttpRequestMessage(HttpMethod.Post, "v1/images/generations")
       {
           Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
       };

       // 3) Send request
       using var resp = await client.SendAsync(req, cancellationToken);
       var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

       if (!resp.IsSuccessStatusCode)
           throw new Exception(string.IsNullOrWhiteSpace(raw) ? resp.ReasonPhrase : raw);

       // 4) Parse response
       using var doc = JsonDocument.Parse(raw);
       if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
           throw new Exception("No image data returned");

       List<ResourceLinkBlock> resourceLinks = [];

       int index = 0;
       foreach (var item in data.EnumerateArray())
       {
           if (!item.TryGetProperty("b64_json", out var b64El) || b64El.ValueKind != JsonValueKind.String)
               continue;

           var bytes = Convert.FromBase64String(b64El.GetString()!);

           var graphItem = await requestContext.Server.Upload(
               serviceProvider,
               $"{typed.Filename}-{index + 1}.png",
               BinaryData.FromBytes(bytes),
               cancellationToken);

           if (graphItem != null)
               resourceLinks.Add(graphItem);

           index++;
       }

       return resourceLinks?.ToResourceLinkCallToolResponse();
   });


    [Description("Please fill in the AI image request details.")]
    public class GrokNewImage
    {
        [JsonPropertyName("prompt")]
        [Required]
        [Description("The image prompt. English prompts only")]
        public string Prompt { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Required]
        [Description("The new image file name.")]
        public string Filename { get; set; } = default!;

        [JsonPropertyName("numberOfImages")]
        [Required]
        [Range(1, 10)]
        [Description("The number of images to create.")]
        public int NumberOfImages { get; set; } = 1;
    }

}



public class XAISettings
{
    public string ApiKey { get; set; } = default!;
}
