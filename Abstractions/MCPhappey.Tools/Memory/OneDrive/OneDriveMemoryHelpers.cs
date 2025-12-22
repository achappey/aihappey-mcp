using System.Text;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;
using Microsoft.Kiota.Abstractions;

namespace MCPhappey.Tools.Memory.OneDrive;

internal static class OneDriveMemoryHelpers
{
    public static async Task<Drive?> GetDefaultDriveAsync(this GraphServiceClient client, CancellationToken ct)
        => await client.Me.Drive.GetAsync(cancellationToken: ct);

    // Ensure /memories exists (under Drive root)
    public static async Task<DriveItem> EnsureRootFolderExistsAsync(
      this GraphServiceClient client,
      string driveId,
      CancellationToken ct)
    {
        // Bestaat al? Probeer eerst via path
        try
        {
            var existing = await client.Drives[driveId]
                .Root
                .ItemWithPath(OneDriveMemory.RootFolderName)
                .GetAsync(cancellationToken: ct);

            if (existing != null) return existing;
        }
        catch
        {
            // niet gevonden, we gaan 'm aanmaken
        }

        // Root ophalen
        var root = await client.Drives[driveId].Root.GetAsync(cancellationToken: ct)
                   ?? throw new Exception("Drive root not found.");

        // Aanmaken DIRECT onder root en het aangemaakte item teruggeven
        var created = await client.Drives[driveId]
            .Items[root.Id!]
            .Children
            .PostAsync(
                new DriveItem { Name = OneDriveMemory.RootFolderName, Folder = new Folder() },
                cancellationToken: ct);

        if (created == null || string.IsNullOrEmpty(created.Id))
            throw new Exception("Failed to create /memories folder.");

        return created;
    }

    public static async Task<string?> ReadTextFileAsync(
        this GraphServiceClient client,
        string driveId,
        string path,
        CancellationToken ct)
    {
        try
        {
            var stream = await client.Drives[driveId].Root.ItemWithPath(path.Trim('/')).Content.GetAsync(cancellationToken: ct);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
    public static async Task<List<string>> ListFilesAsync(
        this GraphServiceClient client,
        string driveId,
        string dirPath,
        CancellationToken ct)
    {
        try
        {
            var p = dirPath.Trim('/');

            // Speciaal geval: exact "/memories" -> lijst op via ID, geen path-lookup
            if (string.Equals(p, OneDriveMemory.RootFolderName, StringComparison.OrdinalIgnoreCase))
            {
                var memories = await client.EnsureRootFolderExistsAsync(driveId, ct);
                var itemsAtRoot = await client.Drives[driveId]
                    .Items[memories.Id!]
                    .Children
                    .GetAsync(cancellationToken: ct);

                return itemsAtRoot?.Value?
                    .Select(i => i.Name ?? string.Empty)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList() ?? new List<string>();
            }

            // Overige paden onder /memories mogen via path
            var items = await client.Drives[driveId]
                .Root
                .ItemWithPath(p)
                .Children
                .GetAsync(cancellationToken: ct);

            return items?.Value?
                .Select(i => i.Name ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList() ?? new List<string>();
        }
        catch
        {
            // Als folder niet bestaat, geef lege lijst terug
            return new List<string>();
        }
    }


    public static CallToolResult ToMemoryTextResult(this string text)
        => text.ToTextContentBlock().ToCallToolResult();

    public static CallToolResult ToMemoryListResult(this string directoryPath, IEnumerable<string> items)
        => (new StringBuilder()
            .AppendLine($"Directory: {directoryPath}")
            .Append(string.Join("\n", items.Select(i => $"- {i}")))
            .ToString())
            .ToTextContentBlock()
            .ToCallToolResult();

    public static CallToolResult ToMemoryError(this string error)
        => error.ToErrorCallToolResponse();


    public static async Task<DriveItem?> WriteTextFileAsync(
   this GraphServiceClient client,
   string driveId,
   string path,
   string content,
   CancellationToken ct)
    {
        return await client.Drives[driveId]
            .Root
            .ItemWithPath(path.Trim('/'))
            .Content
            .PutAsync(BinaryData.FromString(content).ToStream(), cancellationToken: ct);
    }

    public static async Task DeleteFileAsync(
        this GraphServiceClient client,
        string driveId,
        string path,
        CancellationToken ct)
    {
        try
        {
            await client.Drives[driveId]
                .Root
                .ItemWithPath(path.Trim('/'))
                .DeleteAsync(cancellationToken: ct);
        }
        catch
        {
            // deleting non-existent file should not break agent flow
        }
    }
    public static async Task EnsureFolderExistsAsync(
        this GraphServiceClient client,
        string driveId,
        string path,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        // Haal root op (voor parentId wanneer we direct onder root moeten aanmaken)
        var root = await client.Drives[driveId].Root.GetAsync(cancellationToken: ct)
                   ?? throw new Exception("Drive root not found.");
        var currentPath = ""; // opgebouwd pad, bv "memories/users"

        foreach (var part in parts)
        {
            var targetPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";

            // Bestaat target al?
            try
            {
                var existing = await client.Drives[driveId]
                    .Root
                    .ItemWithPath(targetPath)
                    .GetAsync(cancellationToken: ct);

                if (existing != null && existing.Folder != null)
                {
                    currentPath = targetPath;
                    continue;
                }
            }
            catch
            {
                // niet gevonden -> we gaan 'm aanmaken
            }

            // Bepaal parentId: root of huidige parent-pad
            string parentId;
            if (string.IsNullOrEmpty(currentPath))
            {
                parentId = root.Id!;
            }
            else
            {
                var parent = await client.Drives[driveId]
                    .Root
                    .ItemWithPath(currentPath)
                    .GetAsync(cancellationToken: ct)
                    ?? throw new Exception($"Parent folder '{currentPath}' not found.");

                parentId = parent.Id!;
            }

            // Maak de folder onder de ouder via Items[parentId].Children.PostAsync(...)
            await client.Drives[driveId]
                .Items[parentId]
                .Children
                .PostAsync(
                    new DriveItem
                    {
                        Name = part,
                        Folder = new Folder()
                    },
                    cancellationToken: ct);

            currentPath = targetPath;
        }
    }


}
