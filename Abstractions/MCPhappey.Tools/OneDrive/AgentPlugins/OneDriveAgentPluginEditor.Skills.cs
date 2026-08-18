using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

public static partial class OneDriveAgentPluginEditor
{
    [Description("Import and snapshot one complete validated Agent Skill into skills/<name>/ from a shared OneDrive/SharePoint folder or ZIP URL. Re-importing replaces only that embedded skill snapshot.")]
    [McpServerTool(Title = "Add Linked Agent Skill to OneDrive Agent Plugin",
        Name = "onedrive_plugin_editor_add_skill_from_link",
        ReadOnly = false, Idempotent = true, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> AddSkillFromLink(
        string pluginName,
        string? sourceUrl,
        IServiceProvider services,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var (typed, notAccepted, _) = await context.Server.TryElicit(new PluginSkillImportInput
            {
                SourceUrl = sourceUrl ?? string.Empty
            }, cancellationToken);
            if (notAccepted is not null) return notAccepted;
            ArgumentNullException.ThrowIfNull(typed);
            if (!Uri.TryCreate(typed.SourceUrl, UriKind.Absolute, out var sourceUri))
                throw new ValidationException("sourceUrl must be an absolute URL.");

            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);

            AgentSkillSnapshot snapshot;
            if (IsMicrosoftSharingHost(sourceUri.Host))
            {
                var shared = await graph.ResolveSharingLinkAsync(typed.SourceUrl, cancellationToken);
                if (shared.Folder is not null)
                {
                    var sourceFiles = await graph.DownloadSharedFolderAsync(shared, cancellationToken);
                    snapshot = AgentSkillSnapshot.FromSharedFolder(shared.Name ?? string.Empty, sourceFiles);
                }
                else
                {
                    if (shared.File is null
                        || string.IsNullOrWhiteSpace(shared.Id)
                        || string.IsNullOrWhiteSpace(shared.ParentReference?.DriveId))
                        throw new ValidationException("Microsoft sharing link must resolve to a skill folder or ZIP file.");
                    await using var stream = await graph.Drives[shared.ParentReference.DriveId].Items[shared.Id].Content
                        .GetAsync(cancellationToken: cancellationToken)
                        ?? throw new ValidationException("Could not download the shared ZIP file.");
                    using var output = new MemoryStream();
                    await stream.CopyToAsync(output, cancellationToken);
                    snapshot = AgentSkillSnapshot.FromZip(output.ToArray());
                }
            }
            else
            {
                var downloaded = (await services.GetRequiredService<DownloadService>()
                    .DownloadContentAsync(services, context.Server, typed.SourceUrl, cancellationToken)).FirstOrDefault()
                    ?? throw new ValidationException("Could not download the skill ZIP URL.");
                snapshot = AgentSkillSnapshot.FromZip(downloaded.Contents.ToArray());
            }

            await ReplaceSkillSnapshotAsync(graph, drive.Id!, name, snapshot, cancellationToken);
            var affectedFiles = snapshot.Files.Select(file => $"skills/{snapshot.Name}/{file.Path}").ToArray();
            return new
            {
                success = true,
                pluginName = name,
                skillName = snapshot.Name,
                description = snapshot.Description,
                affectedFiles,
                warnings = snapshot.Warnings
            }.ToJsonContentBlock(PluginUri(name, $"skills/{snapshot.Name}")).ToCallToolResult();
        }));

    [Description("Remove one embedded Agent Skill snapshot without changing its linked source.")]
    [McpServerTool(Title = "Remove Agent Skill from OneDrive Agent Plugin",
        Name = "onedrive_plugin_editor_remove_skill",
        ReadOnly = false, Idempotent = true, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> RemoveSkill(
        string pluginName,
        string skillName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        {
            var name = RequirePluginName(pluginName);
            var normalizedSkillName = skillName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedSkillName)
                || normalizedSkillName.Contains('/')
                || normalizedSkillName.Contains('\\')
                || normalizedSkillName.Contains("..", StringComparison.Ordinal))
                throw new ValidationException("skillName must be one embedded skill folder name.");

            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                ?? throw new InvalidOperationException("Could not resolve default OneDrive.");
            await EnsurePluginExistsAsync(graph, drive.Id!, name, cancellationToken);
            var path = OneDriveAgentPluginStorage.PluginPath(name, $"skills/{normalizedSkillName}");
            if (await graph.GetItemByPathOrNullAsync(drive.Id!, path, cancellationToken) is null)
                throw new ValidationException($"Embedded Agent Skill '{normalizedSkillName}' was not found.");

            return await context.ConfirmAndDeleteAsync<DeleteByNameConfirmation>(
                normalizedSkillName,
                async _ => await graph.DeleteItemIfExistsAsync(drive.Id!, path, cancellationToken),
                $"Embedded Agent Skill '{normalizedSkillName}' removed from Agent Plugin '{name}'.",
                cancellationToken);
        }));

    private static async Task ReplaceSkillSnapshotAsync(
        Microsoft.Graph.Beta.GraphServiceClient graph,
        string driveId,
        string pluginName,
        AgentSkillSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var importFolderName = $".skill-import-{Guid.NewGuid():N}";
        var importRoot = $"{OneDriveAgentPluginStorage.PluginRoot(pluginName)}/{importFolderName}";
        var stagedSkill = $"{importRoot}/{snapshot.Name}";
        try
        {
            await graph.EnsurePluginFolderAsync(driveId, stagedSkill, cancellationToken);
            foreach (var file in snapshot.Files)
                await graph.WriteBytesAsync(driveId, $"{stagedSkill}/{file.Path}", file.Bytes, cancellationToken);

            var skillsRoot = $"{OneDriveAgentPluginStorage.PluginRoot(pluginName)}/skills";
            await graph.EnsurePluginFolderAsync(driveId, skillsRoot, cancellationToken);
            await graph.DeleteItemIfExistsAsync(driveId, $"{skillsRoot}/{snapshot.Name}", cancellationToken);
            await graph.MoveItemAsync(driveId, stagedSkill, skillsRoot, snapshot.Name, cancellationToken);
        }
        finally
        {
            await graph.DeleteItemIfExistsAsync(driveId, importRoot, cancellationToken);
        }
    }

    private static bool IsMicrosoftSharingHost(string host)
        => host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
           || host.Equals("1drv.ms", StringComparison.OrdinalIgnoreCase)
           || host.Equals("onedrive.live.com", StringComparison.OrdinalIgnoreCase);
}
