using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OneDrive;

public static class GraphOneDrive
{
    [Description("Uploads a file to the specified OneDrive location.")]
    [McpServerTool(Title = "Upload file to OneDrive",
        Name = "graph_onedrive_upload_file",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDrive_UploadFile(
        [Description("The OneDrive Drive ID.")] string driveId,
        [Description("The file name (e.g. foo.txt).")] string filename,
        [Description("The folder path in OneDrive (e.g. docs).")] string path,
        [Description("The file contents as a string.")] string content,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new GraphUploadFile
              {
                  Name = filename,
                  Path = path,
                  Content = content
              },
              cancellationToken);

        var graphItem = await client.Drives[driveId]
                .Items["root"].ItemWithPath($"/{typed?.Path}/{typed?.Name}")
                .Content.PutAsync(BinaryData.FromString(typed?.Content ?? string.Empty).ToStream(),
                   cancellationToken: cancellationToken);

        return graphItem.ToJsonContentBlock($"https://graph.microsoft.com/beta/drives/{driveId}/items/root:/{path}/{filename}:/content")
         .ToCallToolResult();
    }));

    [Description("Create a folder in the specified OneDrive or SharePoint document library.")]
    [McpServerTool(Title = "Create OneDrive/SharePoint folder",
        Name = "graph_onedrive_create_folder",
        OpenWorld = false, Destructive = true, Idempotent = true)]
    public static async Task<CallToolResult?> GraphOneDrive_CreateFolder(
            [Description("The OneDrive or SharePoint Drive ID.")] string driveId,
            [Description("The name of the new folder.")] string name,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Folder path within the document library. Leave empty for root. Use slashes for subfolders, e.g. 'Invoices/2025'.")]
            string? parentPath = "",
            [Description("The ID of the content type (for Document Set, optional).")]
            string? contentTypeId = null,
            CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
        new GraphNewFolder
        {
            Name = name,
            ContentTypeId = contentTypeId,
        },
        cancellationToken);

        if (notAccepted != null) return notAccepted;


        // Maak de DriveItem voor de folder
        var folderItem = new DriveItem
        {
            Name = typed?.Name,
            Folder = new Folder(),
            AdditionalData = new Dictionary<string, object>
            {
                ["@microsoft.graph.conflictBehavior"] = "fail"
            }
        };

        DriveItem? createdFolder;

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            // Voor root: eerst de root DriveItem ophalen
            var rootItem = await graphClient.Drives[driveId].Root.GetAsync(cancellationToken: cancellationToken);

            // Dan children toevoegen via Items[rootId]
            createdFolder = await graphClient.Drives[driveId]
                .Items[rootItem?.Id]
                .Children
                .PostAsync(folderItem, cancellationToken: cancellationToken);
        }
        else
        {
            // Voor specifiek pad: gebruik ItemWithPath
            createdFolder = await graphClient.Drives[driveId]
                .Root
                .ItemWithPath(parentPath.Trim('/'))
                .Children
                .PostAsync(folderItem, cancellationToken: cancellationToken);
        }
        // ContentType instellen als nodig
        if (!string.IsNullOrEmpty(contentTypeId))
        {
            await SetFolderContentType(graphClient, createdFolder, contentTypeId, cancellationToken);
        }

        return createdFolder
            .ToJsonContentBlock($"https://graph.microsoft.com/beta/drives/{driveId}/items/root:/{typed?.Name}")
            .ToCallToolResult();
    }));

    // Helper voor contenttype
    private static async Task SetFolderContentType(
        GraphServiceClient graphClient,
        DriveItem? folder,
        string contentTypeId,
        CancellationToken cancellationToken)
    {
        var updatedFolder = await graphClient.Drives[folder?.ParentReference?.DriveId]
               .Items[folder?.Id]
               .GetAsync(requestConfiguration =>
               {
                   requestConfiguration.QueryParameters.Expand = ["listItem"];
               }, cancellationToken);

        await graphClient.Sites[folder?.ParentReference?.SharepointIds?.SiteId]
            .Lists[folder?.ParentReference?.SharepointIds?.ListId]
            .Items[updatedFolder?.ListItem?.Id]
            .PatchAsync(new ListItem
            {
                ContentType = new ContentTypeInfo { Id = contentTypeId }
            }, cancellationToken: cancellationToken);
    }

    [Description("Please fill in the new File details.")]
    public class GraphUploadFile
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The name of the new file.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("path")]
        [Required]
        [Description("The path of the new file.")]
        public string Path { get; set; } = default!;

        [JsonPropertyName("content")]
        [Required]
        [Description("The content of the new file.")]
        public string Content { get; set; } = default!;

    }

    [Description("Please fill in the new Folder details.")]
    public class GraphNewFolder
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The name of the new folder.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("contentTypeId")]
        [Description("The id of the content type.")]
        public string? ContentTypeId { get; set; }
    }
}