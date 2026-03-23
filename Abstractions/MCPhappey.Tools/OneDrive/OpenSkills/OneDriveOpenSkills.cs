using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OneDrive.OpenSkills;

public static class OneDriveOpenSkills
{
    internal const string RootFolderName = "skills";
    internal const string SkillManifestName = "SKILL.md";

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
        await context.WithStructuredContent(async () =>
    {
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureFolderExistsAsync(drive.Id!, RootFolderName, cancellationToken);
        var skillFolders = await graph.ListFoldersIfExistsAsync(drive.Id!, RootFolderName, cancellationToken);
        if (skillFolders.Count == 0)
        {
            return new { items = Array.Empty<List<OpenSkillSummary>>() }
            .ToStructuredContent();
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

            var manifest = SkillDocumentParser.Parse(skillText, skillName);

            results.Add(new OpenSkillSummary
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? skillName : manifest.Name,
                Description = manifest.Description,
                Metadata = manifest.Metadata.Count == 0 ? null : manifest.Metadata,
                Warnings = manifest.Warnings.Count == 0 ? null : manifest.Warnings
            });
        }

        return new { items = results }.ToStructuredContent();
    })));

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

    internal static string NormalizeSkillName(string skillName)
    {
        var normalized = skillName.Trim().Replace("\\", "/").Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(".."))
            throw new Exception("Invalid skill name.");
        if (normalized.Contains('/'))
            throw new Exception("Skill name must be a single folder name.");
        return normalized;
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Trim().Replace("\\", "/").Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains(".."))
            throw new Exception("Invalid relative path.");
        return normalized;
    }

    private sealed class OpenSkillSummary
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
        public List<string>? Warnings { get; set; }
    }
}

