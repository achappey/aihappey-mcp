using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mistral.Images;

public static class MistralImages
{
    [Description("Create an image with Mistral AI image generation.")]
    [McpServerTool(Title = "Mistral create image",
        Name = "mistral_images_create",
        IconSource = MistralConstants.ICON_SOURCE,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> MistralImages_Create(
            IServiceProvider serviceProvider,
          [Description("Prompt to create the image.")]
            string prompt,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Target model (e.g. mistral-large-latest or mistral-medium-latest).")]
            string model = "mistral-medium-latest",
          CancellationToken cancellationToken = default)
    {
        var response = await requestContext.Server.SampleAsync(
             new CreateMessageRequestParams()
             {
                 Metadata = new JsonObject
                 {
                     ["mistral"] = new JsonObject
                     {
                         ["image_generation"] = new JsonObject
                         {
                             ["type"] = "image_generation"
                         }
                     }
                 },
                 Temperature = 0,
                 MaxTokens = 8192,
                 ModelPreferences = model.ToModelPreferences(),
                 Messages = [prompt.ToUserSamplingMessage()]
             },
             cancellationToken);

        var metadata = response.Meta?.ToJsonContent("https://api.mistral.ai");

        return await requestContext.WithUploads(response, serviceProvider, metadata, cancellationToken);
    }
}

