using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ClientModel;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.OpenAI.Containers;

public static partial class OpenAIContainers
{

    [Description("Add a file to an OpenAI file container")]
    [McpServerTool(Title = "Add file to container", Destructive = false)]
    public static async Task<CallToolResult?> OpenAIContainers_AddContainerFile(
     [Description("File url of the input file. This tool can access secure SharePoint and OneDrive links.")] string fileUrl,
     [Description("Id of the OpenAI container.")] string containerId,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new Exception();
        var dataUri = file.ToDataUri();

        var resultImage = await openAiClient
            .GetContainerClient()
            .UploadDataUriAsync(containerId, dataUri, file?.MimeType);

        var response = resultImage.GetRawResponse();

        return response.Content.ToString()?
            .ToJsonCallToolResponse($"https://api.openai.com/v1/containers/{containerId}/files/file_id");
    });
    /*

   [Description("Download a file from OpenAI container and uploads it to users' OneDrive")]
   [McpServerTool(Title = "Download OpenAI container file to OneDrive", Name = "openai_containers_file_to_onedrive", Destructive = false)]
   public static async Task<CallToolResult?> OpenAIContainers_FileToOneDrive(
       [Description("Id of the OpenAI container.")] string containerId,
       [Description("Id of the OpenAI file.")] string fileId,
       [Description("File name with extension. Make you add the correct file extension according to the filetype. No .bin files.")] string filename,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) => await requestContext.WithExceptionCheck(async () =>
   {
       var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();

       var client = openAiClient.GetContainerClient();
       var file = await client.GetContainerFileContentAsync(containerId, fileId, cancellationToken);

       var uploaded = await requestContext.Server.Upload(
           serviceProvider,
           filename,
           file.Value,
           cancellationToken);

       return uploaded?.ToResourceLinkCallToolResponse();
   });*/

    [Description("Create a container at OpenAI")]
    [McpServerTool(Title = "Create a container", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIContainers_Create(
        [Description("The container name.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        =>
        await requestContext.WithExceptionCheck(async () =>
        await serviceProvider.WithContainerClient(async (client) =>
        {
            var userId = serviceProvider.GetUserId();

            var imageInput = new OpenAINewContainer
            {
                Name = name
            };

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(imageInput, cancellationToken);

            var payload = new Dictionary<string, object?>
            {
                ["name"] = typed.Name,
            };

            var content = BinaryContent.Create(BinaryData.FromObjectAsJson(payload));
            var item = await client.CreateContainerAsync(content);
            using var raw = item.GetRawResponse();            // PipelineResponse
            string json = raw.Content.ToString();

            return json?.ToJsonContentBlock($"{ContainerExtensions.BASE_URL}").ToCallToolResult();
        }));

    [Description("Please fill in the container details.")]
    public class OpenAINewContainer
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The container name.")]
        public string Name { get; set; } = default!;

    }

}

