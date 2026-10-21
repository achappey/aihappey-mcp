using System.Text.Json;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;

namespace MCPhappey.Tools.Memory.GitHubCopilot;

internal sealed class GitHubCopilotMemoryStore(GraphServiceClient graph, TimeProvider? timeProvider = null)
{
    internal const string FolderName = "GitHubCopilotMemory";
    internal const int DefaultLimit = 20;
    internal const int MaximumLimit = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<GitHubCopilotMemoryEntry>> RecallAsync(
        int? limit = null, CancellationToken cancellationToken = default)
    {
        var count = limit ?? DefaultLimit;
        if (count is < 1 or > MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {MaximumLimit}.");

        var driveId = await GetDriveIdAsync(cancellationToken);
        var folder = await FindFolderAsync(driveId, cancellationToken);
        if (folder is null) return [];

        var memories = new List<GitHubCopilotMemoryEntry>();
        var page = await graph.Drives[driveId].Items[folder.Id!].Children.GetAsync(cancellationToken: cancellationToken);
        while (page is not null)
        {
            foreach (var item in page.Value ?? [])
            {
                // Ignore unrelated files and folders that the user may have added manually.
                if (item.File is null || item.Name is null || !item.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || !Guid.TryParseExact(item.Name[..^5], "D", out var id))
                    continue;

                var memory = await ReadAsync(driveId, item, id.ToString("D"), cancellationToken);
                if (memory is not null) memories.Add(memory);
            }

            if (string.IsNullOrWhiteSpace(page.OdataNextLink)) break;
            page = await graph.Drives[driveId].Items[folder.Id!].Children
                .WithUrl(page.OdataNextLink).GetAsync(cancellationToken: cancellationToken);
        }

        var now = clock.GetUtcNow();
        return memories.Where(m => m.ExpiresAt > now)
            .OrderByDescending(m => m.UpdatedAt)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(count).ToArray();
    }

    public async Task<GitHubCopilotMemoryEntry> StoreAsync(
        string subject, string fact, string[] citations, string reason, string? id = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (citations is null || citations.Length == 0 || citations.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty citation reference is required.", nameof(citations));

        var memoryId = id is null ? Guid.NewGuid().ToString("D") : NormalizeId(id);
        var driveId = await GetDriveIdAsync(cancellationToken);
        var folder = await FindFolderAsync(driveId, cancellationToken);
        DriveItem? existingItem = null;
        GitHubCopilotMemoryEntry? existing = null;
        if (id is not null)
        {
            if (folder is not null)
                existingItem = await FindMemoryAsync(driveId, folder.Id!, memoryId, cancellationToken);
            if (existingItem is not null)
                existing = await ReadAsync(driveId, existingItem, memoryId, cancellationToken);
            if (existing is null) throw new KeyNotFoundException($"Memory '{memoryId}' was not found.");
        }
        else if (folder is null)
        {
            folder = await CreateFolderAsync(driveId, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var memory = new GitHubCopilotMemoryEntry(memoryId, subject.Trim(), fact.Trim(),
            citations.Select(c => c.Trim()).ToArray(), reason.Trim(), existing?.CreatedAt ?? now, now, now.AddDays(28));

        using var content = BinaryData.FromString(JsonSerializer.Serialize(memory, JsonOptions)).ToStream();
        if (existingItem is not null)
        {
            // Do not overwrite a concurrent update or resurrect an item deleted since it was read.
            await graph.Drives[driveId].Items[existingItem.Id!].Content.PutAsync(content,
                config => config.Headers.Add("If-Match", existingItem.ETag ?? "*"), cancellationToken);
        }
        else
        {
            await graph.Drives[driveId].Items[folder!.Id!].ItemWithPath($"{memoryId}.json")
                .Content.PutAsync(content, cancellationToken: cancellationToken);
        }

        return memory;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var memoryId = NormalizeId(id);
        var driveId = await GetDriveIdAsync(cancellationToken);
        var folder = await FindFolderAsync(driveId, cancellationToken);
        if (folder is null) return false;
        var item = await FindMemoryAsync(driveId, folder.Id!, memoryId, cancellationToken);
        if (item is null) return false;

        try
        {
            await graph.Drives[driveId].Items[item.Id!].DeleteAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return false;
        }
    }

    private async Task<string> GetDriveIdAsync(CancellationToken ct)
        => (await graph.GetDefaultDriveAsync(ct))?.Id
            ?? throw new InvalidOperationException("Could not resolve the authenticated user's OneDrive.");

    private async Task<DriveItem?> FindFolderAsync(string driveId, CancellationToken ct)
    {
        try
        {
            var folder = await graph.Drives[driveId].Root.ItemWithPath(FolderName).GetAsync(cancellationToken: ct)
                ?? throw new InvalidOperationException("OneDrive returned an empty memory folder response.");
            if (folder.Folder is null || string.IsNullOrWhiteSpace(folder.Id))
                throw new InvalidOperationException($"/{FolderName} must be a folder.");
            return folder;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<DriveItem> CreateFolderAsync(string driveId, CancellationToken ct)
    {
        var root = await graph.Drives[driveId].Root.GetAsync(cancellationToken: ct)
            ?? throw new InvalidOperationException("OneDrive root was not found.");
        try
        {
            var folder = await graph.Drives[driveId].Items[root.Id!].Children.PostAsync(new DriveItem
            {
                Name = FolderName,
                Folder = new Folder(),
                AdditionalData = new Dictionary<string, object> { ["@microsoft.graph.conflictBehavior"] = "fail" }
            }, cancellationToken: ct);
            if (folder?.Folder is null || string.IsNullOrWhiteSpace(folder.Id))
                throw new InvalidOperationException("Could not create the OneDrive memory folder.");
            return folder;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 409)
        {
            // Another request may have created the dedicated folder concurrently.
            return await FindFolderAsync(driveId, ct)
                ?? throw new InvalidOperationException("Could not resolve the concurrently created memory folder.", ex);
        }
    }

    private async Task<DriveItem?> FindMemoryAsync(string driveId, string folderId, string id, CancellationToken ct)
    {
        try
        {
            var item = await graph.Drives[driveId].Items[folderId].ItemWithPath($"{id}.json")
                .GetAsync(cancellationToken: ct);
            if (item?.File is null || string.IsNullOrWhiteSpace(item.Id))
                throw new InvalidDataException($"Memory '{id}' is not a file.");
            return item;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<GitHubCopilotMemoryEntry?> ReadAsync(string driveId, DriveItem item, string id, CancellationToken ct)
    {
        try
        {
            using var content = await graph.Drives[driveId].Items[item.Id!].Content.GetAsync(cancellationToken: ct)
                ?? throw new InvalidDataException($"Memory '{id}' has no content.");
            var memory = await JsonSerializer.DeserializeAsync<GitHubCopilotMemoryEntry>(content, JsonOptions, ct);
            if (memory is null || memory.Id != id || string.IsNullOrWhiteSpace(memory.Subject)
                || string.IsNullOrWhiteSpace(memory.Fact) || string.IsNullOrWhiteSpace(memory.Reason)
                || memory.Citations is not { Length: > 0 } || memory.Citations.Any(string.IsNullOrWhiteSpace)
                || memory.CreatedAt == default || memory.UpdatedAt < memory.CreatedAt
                || memory.ExpiresAt != memory.UpdatedAt.AddDays(28))
                throw new InvalidDataException($"Memory '{id}' contains an invalid document.");
            return memory;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            // A memory may be deleted between listing/lookup and reading its content.
            return null;
        }
    }

    private static string NormalizeId(string id)
        => Guid.TryParseExact(id, "D", out var parsed)
            ? parsed.ToString("D")
            : throw new ArgumentException("Memory id must be a GUID returned by copilot_memory_store.", nameof(id));
}
