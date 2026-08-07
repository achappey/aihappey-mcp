using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OneDrive;

public static class GraphOneDrive
{

    [Description(
        "Copy a file between OneDrive or SharePoint document libraries. " +
        "The destination file may use a different file name.")]
    [McpServerTool(
        Title = "Copy OneDrive/SharePoint file",
        Name = "graph_onedrive_copy_file",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDrive_CopyFile(
        RequestContext<CallToolRequestParams> requestContext,

        [Description("Drive ID containing the source file.")]
    string? sourceDriveId = null,

        [Description("Drive item ID of the source file.")]
    string? sourceItemId = null,

        [Description("Drive ID of the destination document library.")]
    string? destinationDriveId = null,

        [Description(
        "Destination folder path relative to the document library root. " +
        "Leave empty to copy to the root. Example: 'SiteAssets' or 'Documents/Logos'.")]
    string? destinationFolderPath = null,

        [Description(
        "File name to use at the destination, including extension. " +
        "This may differ from the source file name.")]
    string? destinationFileName = null,

        CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, _) =
                    await requestContext.Server.TryElicit(
                        new GraphCopyFile
                        {
                            SourceDriveId = sourceDriveId,
                            SourceItemId = sourceItemId,
                            DestinationDriveId = destinationDriveId,
                            DestinationFolderPath = destinationFolderPath,
                            DestinationFileName = destinationFileName
                        },
                        cancellationToken);

                if (notAccepted is not null)
                    throw new Exception(JsonSerializer.Serialize(notAccepted));

                typed!.Validate();

                var sourceItem = await graphClient
                    .Drives[typed.SourceDriveId!]
                    .Items[typed.SourceItemId!]
                    .GetAsync(
                        requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select =
                            [
                                "id",
                            "name",
                            "size",
                            "file",
                            "folder"
                            ];
                        },
                        cancellationToken);

                if (sourceItem is null)
                    throw new InvalidOperationException(
                        "Microsoft Graph returned no source file.");

                if (sourceItem.Folder is not null)
                    throw new ValidationException(
                        "The source drive item is a folder. This tool only copies files.");

                if (sourceItem.File is null)
                    throw new ValidationException(
                        "The source drive item is not a file.");

                var finalFileName =
                    !string.IsNullOrWhiteSpace(typed.DestinationFileName)
                        ? typed.DestinationFileName.Trim()
                        : sourceItem.Name;

                if (string.IsNullOrWhiteSpace(finalFileName))
                    throw new ValidationException(
                        "Could not determine the destination file name.");

                await using var sourceStream = await graphClient
                    .Drives[typed.SourceDriveId!]
                    .Items[typed.SourceItemId!]
                    .Content
                    .GetAsync(cancellationToken: cancellationToken);

                if (sourceStream is null)
                    throw new InvalidOperationException(
                        "Microsoft Graph returned no content for the source file.");

                var destinationPath = CombineDrivePath(
                    typed.DestinationFolderPath,
                    finalFileName);

                var copiedItem = await graphClient
                    .Drives[typed.DestinationDriveId!]
                    .Root
                    .ItemWithPath(destinationPath)
                    .Content
                    .PutAsync(
                        sourceStream,
                        cancellationToken: cancellationToken);

                if (copiedItem is null)
                    throw new InvalidOperationException(
                        "Microsoft Graph returned no destination drive item.");

                return new
                {
                    SourceDriveId = typed.SourceDriveId,
                    SourceItemId = typed.SourceItemId,
                    SourceFileName = sourceItem.Name,
                    SourceSize = sourceItem.Size,

                    DestinationDriveId = typed.DestinationDriveId,
                    DestinationFolderPath =
                        NormalizeDriveFolderPath(
                            typed.DestinationFolderPath),

                    DestinationFileName = finalFileName,
                    DestinationPath = destinationPath,

                    DestinationItemId = copiedItem.Id,
                    DestinationWebUrl = copiedItem.WebUrl,
                    DestinationSize = copiedItem.Size,

                    Status = "Copied file successfully."
                };
            })));

    private static string CombineDrivePath(
        string? folderPath,
        string fileName)
    {
        var normalizedFolderPath =
            NormalizeDriveFolderPath(folderPath);

        return string.IsNullOrWhiteSpace(normalizedFolderPath)
            ? fileName
            : $"{normalizedFolderPath}/{fileName}";
    }

    private static string NormalizeDriveFolderPath(
        string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return string.Empty;

        return folderPath
            .Replace('\\', '/')
            .Trim('/');
    }

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
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
              new GraphUploadFile
              {
                  Name = filename,
                  Path = path,
                  Content = content
              },
              cancellationToken);

        return await client.Drives[driveId]
                .Items["root"].ItemWithPath($"/{typed?.Path}/{typed?.Name}")
                .Content.PutAsync(BinaryData.FromString(typed?.Content ?? string.Empty).ToStream(),
                   cancellationToken: cancellationToken);
    })));

    [Description("Create a folder in the specified OneDrive or SharePoint document library.")]
    [McpServerTool(Title = "Create OneDrive/SharePoint folder",
        Name = "graph_onedrive_create_folder",
        OpenWorld = false,
        Destructive = true,
        Idempotent = true)]
    public static async Task<CallToolResult?> GraphOneDrive_CreateFolder(
            [Description("The OneDrive or SharePoint Drive ID.")] string driveId,
            [Description("The name of the new folder.")] string name,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Folder path within the document library. Leave empty for root. Use slashes for subfolders, e.g. 'Invoices/2025'.")]
            string? parentPath = "",
            [Description("The ID of the content type (for Document Set, optional).")]
            string? contentTypeId = null,
            CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
        new GraphNewFolder
        {
            Name = name,
            ContentTypeId = contentTypeId,
        },
        cancellationToken);

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

        return createdFolder;
    })));

    [Description("Rename a file or folder in OneDrive or a SharePoint document library.")]
    [McpServerTool(Title = "Rename OneDrive/SharePoint item", Name = "graph_onedrive_rename_item",
        UseStructuredContent = true, OutputSchemaType = typeof(DriveItem), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDrive_RenameItem(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID to rename.")] string itemId,
        [Description("New file or folder name.")] string name,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphRenameDriveItem { Name = name }, cancellationToken);
            if (notAccepted is not null)
                return default(DriveItem);

            return await client.Drives[driveId].Items[itemId].PatchAsync(
                new DriveItem { Name = typed?.Name }, cancellationToken: cancellationToken);
        })));

    [Description("Move a file or folder to another folder in the same OneDrive or SharePoint drive.")]
    [McpServerTool(Title = "Move OneDrive/SharePoint item", Name = "graph_onedrive_move_item",
        UseStructuredContent = true, OutputSchemaType = typeof(DriveItem), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDrive_MoveItem(
        [Description("Drive ID containing the item and destination folder.")] string driveId,
        [Description("Drive item ID to move.")] string itemId,
        [Description("Destination folder drive item ID.")] string destinationFolderId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphMoveDriveItem { DestinationFolderId = destinationFolderId }, cancellationToken);
            if (notAccepted is not null)
                return default(DriveItem);

            return await client.Drives[driveId].Items[itemId].PatchAsync(
                new DriveItem { ParentReference = new ItemReference { Id = typed?.DestinationFolderId } },
                cancellationToken: cancellationToken);
        })));

    [Description("Delete a file or folder from OneDrive or a SharePoint document library.")]
    [McpServerTool(Title = "Delete OneDrive/SharePoint item", Name = "graph_onedrive_delete_item",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDrive_DeleteItem(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID to delete.")] string itemId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteDriveItem>(
            itemId,
            async _ => await client.Drives[driveId].Items[itemId].DeleteAsync(cancellationToken: cancellationToken),
            "Drive item deleted.", cancellationToken));

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

    [Description("Please provide the new file or folder name.")]
    public class GraphRenameDriveItem
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("New file or folder name.")]
        public string Name { get; set; } = default!;
    }

    [Description("Please provide the destination folder.")]
    public class GraphMoveDriveItem
    {
        [JsonPropertyName("destinationFolderId")]
        [Required]
        [Description("Destination folder drive item ID.")]
        public string DestinationFolderId { get; set; } = default!;
    }

    [Description("Please confirm the drive item id to delete: {0}")]
    public class GraphDeleteDriveItem : MCPhappey.Common.Models.IHasName
    {
        [Required]
        [Description("The drive item id.")]
        public string Name { get; set; } = default!;
    }

    [Description(
    "Confirm copying a file between OneDrive or SharePoint document libraries.")]
    public class GraphCopyFile
    {
        [JsonPropertyName("sourceDriveId")]
        [Required]
        [Description("Drive ID containing the source file.")]
        public string? SourceDriveId { get; set; }

        [JsonPropertyName("sourceItemId")]
        [Required]
        [Description("Drive item ID of the source file.")]
        public string? SourceItemId { get; set; }

        [JsonPropertyName("destinationDriveId")]
        [Required]
        [Description("Drive ID of the destination document library.")]
        public string? DestinationDriveId { get; set; }

        [JsonPropertyName("destinationFolderPath")]
        [Description(
            "Destination folder path relative to the document library root. " +
            "Leave empty for root.")]
        public string? DestinationFolderPath { get; set; }

        [JsonPropertyName("destinationFileName")]
        [Description(
            "Optional destination file name including extension. " +
            "Defaults to the source file name.")]
        public string? DestinationFileName { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceDriveId))
                throw new ValidationException(
                    "sourceDriveId is required.");

            if (string.IsNullOrWhiteSpace(SourceItemId))
                throw new ValidationException(
                    "sourceItemId is required.");

            if (string.IsNullOrWhiteSpace(DestinationDriveId))
                throw new ValidationException(
                    "destinationDriveId is required.");

            if (!string.IsNullOrWhiteSpace(DestinationFileName))
            {
                var fileName = DestinationFileName.Trim();

                if (fileName.Contains('/') ||
                    fileName.Contains('\\'))
                {
                    throw new ValidationException(
                        "destinationFileName must be a file name only, not a path.");
                }

                if (fileName is "." or "..")
                {
                    throw new ValidationException(
                        "destinationFileName is invalid.");
                }
            }

            if (!string.IsNullOrWhiteSpace(DestinationFolderPath))
            {
                var segments = DestinationFolderPath
                    .Replace('\\', '/')
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);

                if (segments.Any(segment => segment is "." or ".."))
                {
                    throw new ValidationException(
                        "destinationFolderPath cannot contain '.' or '..' path segments.");
                }
            }
        }
    }
}
