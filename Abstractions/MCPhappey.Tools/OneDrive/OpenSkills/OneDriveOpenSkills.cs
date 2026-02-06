using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OneDrive.OpenSkills;

public static class OneDriveOpenSkills
{
    private const string RootFolderName = "skills";
    private const string SkillManifestName = "SKILL.md";

    [Description("List OpenSkills stored under /skills in the user's OneDrive.")]
    [McpServerTool(
        Title = "List OneDrive OpenSkills",
        Name = "onedrive_openskills_list",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveOpenSkills_List(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var skillFolders = await graph.ListFoldersIfExistsAsync(drive.Id!, RootFolderName, cancellationToken);
        if (skillFolders.Count == 0)
        {
            return new List<OpenSkillSummary>()
                .ToJsonContentBlock("onedrive://skills")
                .ToCallToolResult();
        }

        var results = new List<OpenSkillSummary>();

        foreach (var folder in skillFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skillName = folder.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillName))
                continue;

            var skillPath = $"/{RootFolderName}/{skillName}/{SkillManifestName}";
            var skillText = await graph.ReadTextFileAsync(drive.Id!, skillPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(skillText))
            {
                results.Add(new OpenSkillSummary
                {
                    Name = skillName,
                    Description = null,
                    Metadata = null
                });
                continue;
            }

            var (metadata, _) = ParseFrontMatter(skillText);
            metadata.TryGetValue("name", out var manifestName);
            metadata.TryGetValue("description", out var description);

            results.Add(new OpenSkillSummary
            {
                Name = string.IsNullOrWhiteSpace(manifestName) ? skillName : manifestName,
                Description = description,
                Metadata = metadata.Count == 0 ? null : metadata
            });
        }

        return results
            .ToJsonContentBlock("onedrive://skills")
            .ToCallToolResult();
    }));

    [Description("Read the SKILL.md for a specific skill under /skills/{skillName}.")]
    [McpServerTool(
        Title = "Read OneDrive OpenSkills SKILL.md",
        Name = "onedrive_openskills_read",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveOpenSkills_Read(
        [Description("Name of the skill folder under /skills.")] string skillName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new Exception("Skill name is required.");

        var normalizedName = NormalizeSkillName(skillName);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var skillPath = $"/{RootFolderName}/{normalizedName}/{SkillManifestName}";
        var content = await graph.ReadTextFileAsync(drive.Id!, skillPath, cancellationToken);
        if (content is null)
            throw new Exception($"Skill '{normalizedName}' not found or missing SKILL.md.");

        return content.ToTextContentBlock().ToCallToolResult();
    }));

    [Description("Read a file by relative path within a skill folder under /skills/{skillName}.")]
    [McpServerTool(
        Title = "Read OneDrive OpenSkills file",
        Name = "onedrive_openskills_read_file",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveOpenSkills_ReadFile(
        [Description("Name of the skill folder under /skills.")] string skillName,
        [Description("Relative path inside the skill folder (e.g. references/guide.md).")]
        string relativePath,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new Exception("Skill name is required.");
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new Exception("Relative path is required.");

        var normalizedName = NormalizeSkillName(skillName);
        var normalizedRelPath = NormalizeRelativePath(relativePath);

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var filePath = $"/{RootFolderName}/{normalizedName}/{normalizedRelPath}";
        var content = await graph.ReadTextFileAsync(drive.Id!, filePath, cancellationToken);
        if (content is null)
            throw new Exception($"File '{normalizedRelPath}' not found in skill '{normalizedName}'.");

        return content.ToTextContentBlock().ToCallToolResult();
    }));

    private static string NormalizeSkillName(string skillName)
    {
        var normalized = skillName.Trim().Replace("\\", "/").Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(".."))
            throw new Exception("Invalid skill name.");
        if (normalized.Contains('/'))
            throw new Exception("Skill name must be a single folder name.");
        return normalized;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Trim().Replace("\\", "/").Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(".."))
            throw new Exception("Invalid relative path.");
        return normalized;
    }

    private static (Dictionary<string, string> metadata, string content) ParseFrontMatter(string text)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return (metadata, string.Empty);

        using var reader = new StringReader(text);
        var line = reader.ReadLine();
        if (line == null || line.Trim() != "---")
            return (metadata, text);

        while ((line = reader.ReadLine()) != null)
        {
            if (line.Trim() == "---")
                break;

            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            metadata[key] = value;
        }

        var remaining = reader.ReadToEnd();
        return (metadata, remaining ?? string.Empty);
    }

    private sealed class OpenSkillSummary
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}

internal static class OneDriveOpenSkillsGraphExtensions
{
    public static async Task<List<DriveItem>> ListFoldersIfExistsAsync(
        this GraphServiceClient graph,
        string driveId,
        string folderPath,
        CancellationToken ct)
    {
        try
        {
            var path = folderPath.Trim('/');
            var items = await graph.Drives[driveId]
                .Root
                .ItemWithPath(path)
                .Children
                .GetAsync(cancellationToken: ct);

            return items?.Value?
                .Where(i => i.Folder != null)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }
}
