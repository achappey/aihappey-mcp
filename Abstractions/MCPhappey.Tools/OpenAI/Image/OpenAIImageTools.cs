using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Image;

public static class OpenAIImageTools
{

    [Description("OpenAI ask multiple images.")]
    [McpServerTool(Title = "Ask multiple images", Name = "openai_ask_images",
          Destructive = false,
          OpenWorld = true,
          ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_AskImages(
            [Description("Prompt to execute.")]
            string prompt,
           [Description("Image urls. SharePoint/OneDrive linkes are supported")]
            List<string> imageUrls,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            CancellationToken cancellationToken = default)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        List<SamplingMessage> imageBlocks = [];

        foreach (var imageUrl in imageUrls)
        {
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
                imageUrl, cancellationToken);

            var file = files.FirstOrDefault();

            imageBlocks.Add(new ImageContentBlock()
            {
                Data = Convert.ToBase64String(file?.Contents),
                MimeType = file?.MimeType!,
            }.ToUserSamplingMessage());
        }

        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
            {
                {"openai", new {
                    reasoning = new
                        {
                            effort = "low"
                        },
                    } },
            }),
            Temperature = 1,
            MaxTokens = 8192 * 4,
            ModelPreferences = "gpt-5.1".ToModelPreferences(),
            Messages = [.. imageBlocks, prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        return respone.Content;
    }

    [Description("Describes one or more images.")]
    [McpServerTool(
       Title = "Describe images",
       Name = "openai_describe_images",
       ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAI_DescribeImages(
      [Description("Image urls to describe")] List<string> imageUrls,
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Detail level")] ImageDescriptionDetailLevel imageDescriptionDetailLevel = ImageDescriptionDetailLevel.medium,
      CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async ()
      => await requestContext.WithStructuredContent(async () =>
   {
       var mcpServer = requestContext.Server;
       var samplingService = serviceProvider.GetRequiredService<SamplingService>();
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();
       var promptArgs = new Dictionary<string, JsonElement>
       {
           ["detailLevel"] = JsonSerializer.SerializeToElement(imageDescriptionDetailLevel.GetEnumMemberValue())
       };

       List<SamplingMessage> imageBlocks = [];

       foreach (var imageUrl in imageUrls)
       {
           var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
               imageUrl, cancellationToken);

           var file = files.FirstOrDefault();

           imageBlocks.Add(new ImageContentBlock()
           {
               Data = Convert.ToBase64String(file?.Contents),
               MimeType = file?.MimeType!,
           }.ToUserSamplingMessage());
       }

       var startTime = DateTime.UtcNow;
       var result = await samplingService.GetPromptSample(
           serviceProvider,
           mcpServer,
           "describe-images-in-detail",
           arguments: promptArgs,
           modelHint: "gpt-5-mini",
           maxTokens: 8192 * 4,
           metadata: new Dictionary<string, object>
           {
                { "openai", new {
                      reasoning = new
                        {
                            effort = "low"
                        },
                    }
                }
           },
           messages: imageBlocks,
           cancellationToken: cancellationToken
       );

       var endTime = DateTime.UtcNow;
       result.Meta?.Add("duration", (endTime - startTime).ToString());

       return result;
   }));


    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ImageDescriptionDetailLevel
    {
        [Display(Name = "Low")]
        low,

        [Display(Name = "Medium")]
        medium,

        [Display(Name = "Detailed")]
        detailed
    }
}

