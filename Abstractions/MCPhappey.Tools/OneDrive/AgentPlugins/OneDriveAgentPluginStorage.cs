using System.ComponentModel.DataAnnotations;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

internal sealed record AgentPluginFileEntry(
    string Path,
    long Size,
    string? MimeType,
    string? WebUrl);

internal sealed record AgentPluginBinaryFile(
    string Path,
    byte[] Bytes,
    string? MimeType = null);

internal static class OneDriveAgentPluginStorage
{
    public const string RootFolderName = "plugins";
    public const int MaximumEntries = 4096;
    public const long MaximumExpandedBytes = 128L * 1024L * 1024L;

    public static string PluginRoot(string pluginName)
        => $"/{RootFolderName}/{AgentPluginSpecification.NormalizePackagePath(pluginName)}";

    public static string PluginPath(string pluginName, string relativePath)
        => $"{PluginRoot(pluginName)}/{AgentPluginSpecification.NormalizePackagePath(relativePath)}";

    public static async Task<IReadOnlyList<DriveItem>> ListPluginFoldersAsync(
        this GraphServiceClient graph,
        string driveId,
        CancellationToken cancellationToken)
    {
        await graph.EnsurePluginFolderAsync(driveId, RootFolderName, cancellationToken);
        var root = await graph.Drives[driveId].Root.ItemWithPath(RootFolderName).GetAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Could not resolve /plugins in OneDrive.");
        return (await ListChildrenPagedAsync(graph, driveId, root.Id!, cancellationToken))
            .Where(item => item.Folder is not null && !string.IsNullOrWhiteSpace(item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<IReadOnlyList<AgentPluginFileEntry>> ListPluginFilesAsync(
        this GraphServiceClient graph,
        string driveId,
        string pluginName,
        CancellationToken cancellationToken)
    {
        var root = await graph.GetItemByPathOrNullAsync(driveId, PluginRoot(pluginName), cancellationToken)
            ?? throw new ValidationException($"Agent Plugin '{pluginName}' was not found.");
        if (root.Folder is null)
            throw new ValidationException($"Agent Plugin '{pluginName}' is not a folder.");

        var files = new List<AgentPluginFileEntry>();
        await CollectFileEntriesAsync(graph, driveId, root.Id!, string.Empty, files, cancellationToken);
        return files.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
    }

    public static async Task<DriveItem?> GetItemByPathOrNullAsync(
        this GraphServiceClient graph,
        string driveId,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await graph.Drives[driveId].Root.ItemWithPath(path.Trim('/')).GetAsync(cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<byte[]?> ReadBytesAsync(
        this GraphServiceClient graph,
        string driveId,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await graph.Drives[driveId].Root.ItemWithPath(path.Trim('/')).Content
                .GetAsync(cancellationToken: cancellationToken);
            if (stream is null) return null;
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DriveItem?> WriteBytesAsync(
        this GraphServiceClient graph,
        string driveId,
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length > MaximumExpandedBytes)
            throw new ValidationException("File exceeds the Agent Plugin package size limit.");

        var normalized = path.Trim('/').Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash > 0)
            await graph.EnsurePluginFolderAsync(driveId, normalized[..slash], cancellationToken);

        await using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        return await graph.Drives[driveId].Root.ItemWithPath(normalized).Content
            .PutAsync(stream, cancellationToken: cancellationToken);
    }

    public static async Task DeleteItemIfExistsAsync(
        this GraphServiceClient graph,
        string driveId,
        string path,
        CancellationToken cancellationToken)
    {
        var item = await graph.GetItemByPathOrNullAsync(driveId, path, cancellationToken);
        if (item?.Id is null) return;
        await graph.Drives[driveId].Items[item.Id].DeleteAsync(cancellationToken: cancellationToken);
    }

    public static async Task EnsurePluginFolderAsync(
        this GraphServiceClient graph,
        string driveId,
        string path,
        CancellationToken cancellationToken)
    {
        var parts = path.Trim('/').Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var root = await graph.Drives[driveId].Root.GetAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Drive root not found.");
        var parentId = root.Id!;
        var currentPath = string.Empty;
        foreach (var part in parts)
        {
            if (part is "." or ".." || part.Contains('\0'))
                throw new ValidationException("Folder path must remain within the drive root.");
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
            var existing = await graph.GetItemByPathOrNullAsync(driveId, currentPath, cancellationToken);
            if (existing is not null)
            {
                if (existing.Folder is null)
                    throw new ValidationException($"'{currentPath}' exists but is not a folder.");
                parentId = existing.Id!;
                continue;
            }

            var created = await graph.Drives[driveId].Items[parentId].Children.PostAsync(
                new DriveItem { Name = part, Folder = new Folder() },
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Could not create folder '{currentPath}'.");
            parentId = created.Id!;
        }
    }

    public static async Task MoveItemAsync(
        this GraphServiceClient graph,
        string driveId,
        string sourcePath,
        string destinationFolderPath,
        string destinationName,
        CancellationToken cancellationToken)
    {
        var source = await graph.GetItemByPathOrNullAsync(driveId, sourcePath, cancellationToken)
            ?? throw new ValidationException($"Staged item '{sourcePath}' was not found.");
        var destination = await graph.GetItemByPathOrNullAsync(driveId, destinationFolderPath, cancellationToken)
            ?? throw new ValidationException($"Destination folder '{destinationFolderPath}' was not found.");
        if (destination.Folder is null)
            throw new ValidationException($"Destination '{destinationFolderPath}' is not a folder.");

        await graph.Drives[driveId].Items[source.Id!].PatchAsync(new DriveItem
        {
            Name = destinationName,
            ParentReference = new ItemReference { Id = destination.Id, DriveId = driveId }
        }, cancellationToken: cancellationToken);
    }

    public static async Task<DriveItem> ResolveSharingLinkAsync(
        this GraphServiceClient graph,
        string sharingUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sharingUrl, UriKind.Absolute, out _))
            throw new ValidationException("sourceUrl must be an absolute OneDrive or SharePoint sharing URL.");
        var encoded = "u!" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sharingUrl))
            .TrimEnd('=').Replace('/', '_').Replace('+', '-');
        return await graph.Shares[encoded].DriveItem.GetAsync(cancellationToken: cancellationToken)
            ?? throw new ValidationException("Could not resolve the OneDrive or SharePoint sharing link.");
    }

    public static async Task<IReadOnlyList<AgentPluginBinaryFile>> DownloadSharedFolderAsync(
        this GraphServiceClient graph,
        DriveItem sharedFolder,
        CancellationToken cancellationToken)
    {
        if (sharedFolder.Folder is null || string.IsNullOrWhiteSpace(sharedFolder.Id)
            || string.IsNullOrWhiteSpace(sharedFolder.ParentReference?.DriveId))
            throw new ValidationException("The sharing link must resolve to a folder.");

        var files = new List<AgentPluginBinaryFile>();
        long totalBytes = 0;
        await CollectSharedFolderAsync(
            graph,
            sharedFolder.ParentReference.DriveId,
            sharedFolder.Id,
            string.Empty,
            files,
            bytes =>
            {
                totalBytes += bytes;
                if (totalBytes > MaximumExpandedBytes)
                    throw new ValidationException("Shared skill folder exceeds the expanded size limit.");
                if (files.Count >= MaximumEntries)
                    throw new ValidationException("Shared skill folder contains too many files.");
            },
            cancellationToken);
        return files;
    }

    private static async Task CollectFileEntriesAsync(
        GraphServiceClient graph,
        string driveId,
        string folderId,
        string prefix,
        List<AgentPluginFileEntry> sink,
        CancellationToken cancellationToken)
    {
        foreach (var child in await ListChildrenPagedAsync(graph, driveId, folderId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(child.Name)) continue;
            var path = string.IsNullOrEmpty(prefix) ? child.Name : $"{prefix}/{child.Name}";
            if (child.Folder is not null)
            {
                await CollectFileEntriesAsync(graph, driveId, child.Id!, path, sink, cancellationToken);
                continue;
            }

            if (sink.Count >= MaximumEntries)
                throw new ValidationException("Agent Plugin package contains too many files.");
            sink.Add(new(path, child.Size ?? 0, child.File?.MimeType, child.WebUrl));
        }
    }

    private static async Task CollectSharedFolderAsync(
        GraphServiceClient graph,
        string driveId,
        string folderId,
        string prefix,
        List<AgentPluginBinaryFile> sink,
        Action<long> account,
        CancellationToken cancellationToken)
    {
        foreach (var child in await ListChildrenPagedAsync(graph, driveId, folderId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(child.Name)) continue;
            var path = string.IsNullOrEmpty(prefix) ? child.Name : $"{prefix}/{child.Name}";
            if (child.Folder is not null)
            {
                await CollectSharedFolderAsync(graph, driveId, child.Id!, path, sink, account, cancellationToken);
                continue;
            }

            await using var stream = await graph.Drives[driveId].Items[child.Id!].Content.GetAsync(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Could not download shared file '{path}'.");
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, cancellationToken);
            account(output.Length);
            sink.Add(new(path, output.ToArray(), child.File?.MimeType));
        }
    }

    private static async Task<List<DriveItem>> ListChildrenPagedAsync(
        GraphServiceClient graph,
        string driveId,
        string folderId,
        CancellationToken cancellationToken)
    {
        var results = new List<DriveItem>();
        var page = await graph.Drives[driveId].Items[folderId].Children.GetAsync(cancellationToken: cancellationToken);
        while (page is not null)
        {
            results.AddRange(page.Value ?? []);
            if (string.IsNullOrWhiteSpace(page.OdataNextLink)) break;
            page = await graph.Drives[driveId].Items[folderId].Children.WithUrl(page.OdataNextLink)
                .GetAsync(cancellationToken: cancellationToken);
        }
        return results;
    }
}
