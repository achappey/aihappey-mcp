using MCPhappey.Core.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Extensions;

public static class GraphExtensions
{
    public static EmailAddress ToEmailAddress(
        this string mail) => new() { Address = mail.Trim() };

    public static Recipient ToRecipient(
        this string mail) => new() { EmailAddress = mail.ToEmailAddress() };

    /// <summary>
    /// Uploads a binary file to a SharePoint/OneDrive URL (v5 Graph SDK).
    /// Works for any file type.
    /// </summary>
    public static async Task<DriveItem?> UploadBinaryDataAsync(
        this GraphServiceClient graphClient,
        string shareUrl,
        BinaryData content,
        CancellationToken cancellationToken = default)
    {
        // Graph v5: resolve the share URL to a driveItem first
        // Encode the sharing link into an ID (same as Graph API docs)
        var share = await graphClient.GetDriveItem(shareUrl);

        if (share == null)
            throw new IOException($"Could not resolve share URL '{shareUrl}'.");

        // Convert BinaryData to stream
        using var stream = new MemoryStream(content.ToArray());

        // PutAsync on the /content property to upload
        var uploaded = await graphClient
            .Drives[share.ParentReference?.DriveId]
            .Items[share.Id]
            .Content
            .PutAsync(stream, cancellationToken: cancellationToken);

        return uploaded;
    }

    internal static async Task<CallToolResult> WithOboGraphClient(this RequestContext<CallToolRequestParams> requestContext,
       Func<GraphServiceClient, Task<CallToolResult>> func)
    {
        if (requestContext.Services == null)
            throw new ArgumentNullException(nameof(requestContext.Services));

        using var client = await requestContext.Services.GetOboGraphClient(requestContext.Server);

        return await func(client);
    }

    /// <summary>
    /// Returns all file URLs from a SharePoint or OneDrive folder URL.
    /// </summary>
    public static async Task<List<string>> GetFileUrlsFromFolderAsync(
        this GraphServiceClient graphClient,
        string folderUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderUrl);

        var fileUrls = new List<string>();
        var folderItem = await graphClient.GetDriveItem(folderUrl);

        // Skip if not a folder
        if (folderItem?.Folder == null)
            return fileUrls;

        var driveId = folderItem.ParentReference?.DriveId ?? throw new Exception("DriveId missing");
        var folderId = folderItem.Id ?? throw new Exception("FolderId missing");

        var children = await graphClient.Drives[driveId].Items[folderId].Children
            .GetAsync(cancellationToken: cancellationToken);

        foreach (var item in children?.Value?.Where(a => !string.IsNullOrEmpty(a.WebUrl)) ?? Enumerable.Empty<DriveItem>())
        {
            // Include only files (no subfolders)
            if (item.Folder == null && !string.IsNullOrEmpty(item.WebUrl))
                fileUrls.Add(item.WebUrl!);
        }

        return fileUrls;
    }
}
