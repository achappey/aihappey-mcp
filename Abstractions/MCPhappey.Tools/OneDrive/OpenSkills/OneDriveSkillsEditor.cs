using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using MCPhappey.Tools.Memory.OneDrive;
using MCPhappey.Tools.OneDrive.OpenSkills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

public static class OneDriveSkillsEditor
{
    [Description("Please confirm to delete: {0}")]
    public sealed class OneDriveDeleteSkillConfirmation : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion by entering the skill name.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Create a new OneDrive skill. The skill name must follow the Agent Skills naming rules and match the folder name.")]
    public class OneDriveSkillCreateInput
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Skill name. Lowercase letters, numbers, and hyphens only. Must match the skill folder name.")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Required]
        [Description("Describe what the skill does and when to use it. Required by the Agent Skills specification.")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("instructions")]
        [Description("Markdown instructions for the SKILL.md body.")]
        public string? Instructions { get; set; }

        [JsonPropertyName("license")]
        [Description("Optional license text or reference.")]
        public string? License { get; set; }

        [JsonPropertyName("compatibility")]
        [Description("Optional compatibility notes such as runtime or client requirements.")]
        public string? Compatibility { get; set; }

        [JsonPropertyName("allowedTools")]
        [Description("Optional allowed-tools field. Space-delimited list per the spec.")]
        public string? AllowedTools { get; set; }

        [JsonPropertyName("metadata")]
        [Description("Optional metadata lines in key=value format, one per line.")]
        public string? Metadata { get; set; }
    }

    [Description("Update the SKILL.md metadata and instructions for a OneDrive skill.")]
    public sealed class OneDriveSkillManifestInput : OneDriveSkillCreateInput
    {
    }

    [Description("Create or update a file within a OneDrive skill folder.")]
    public sealed class OneDriveSkillFileUpsertInput
    {
        [JsonPropertyName("relativePath")]
        [Required]
        [Description("Relative path inside the skill folder, for example references/REFERENCE.md or assets/template.json.")]
        public string RelativePath { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        [Required]
        [Description("UTF-8 text content to write into the target file.")]
        public string Content { get; set; } = string.Empty;
    }

    [Description("Import a single file into an existing OneDrive skill folder from a direct URL.")]
    public sealed class OneDriveSkillImportInput
    {
        [JsonPropertyName("fileUrl")]
        [Required]
        [Description("Direct file URL to download and copy into the skill folder.")]
        public string FileUrl { get; set; } = string.Empty;

        [JsonPropertyName("relativePath")]
        [Required]
        [Description("Destination relative path inside the skill folder.")]
        public string RelativePath { get; set; } = string.Empty;
    }

    [Description("Delete a single file from a OneDrive skill folder. The SKILL.md manifest cannot be deleted with this tool.")]
    [McpServerTool(
        Title = "Delete OneDrive Skill File",
        Name = "onedrive_skills_editor_delete_file",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_DeleteFile(
        [Description("Name of the skill folder under /skills.")] string skillName,
        [Description("Relative path inside the skill folder.")] string relativePath,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var normalizedPath = OneDriveOpenSkills.NormalizeRelativePath(relativePath);
        if (normalizedPath.Equals(OneDriveOpenSkills.SkillManifestName, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Use the SKILL.md update tool instead of deleting the manifest.");

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var path = $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}/{normalizedPath}";
        await graph.DeleteFileAsync(drive.Id!, path, cancellationToken);
        return $"Deleted '{normalizedPath}' from skill '{normalizedName}'.".ToTextContentBlock().ToCallToolResult();
    }));

    [Description("List files inside a OneDrive skill folder.")]
    [McpServerTool(
        Title = "List OneDrive Skill Files",
        Name = "onedrive_skills_editor_list_files",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_ListFiles(
        [Description("Name of the skill folder under /skills.")] string skillName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
        {
            var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
            var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                    ?? throw new Exception("Could not resolve default OneDrive.");

            var files = await graph.ListFilesRecursivelyIfExistsAsync(
                drive.Id!,
                $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}",
                cancellationToken);

            return new
            {
                skillName = normalizedName,
                files
            }.ToStructuredContent();
        })));

    [Description("Inspect and validate a OneDrive skill manifest against the Agent Skills specification.")]
    [McpServerTool(
        Title = "Inspect OneDrive Skill Manifest",
        Name = "onedrive_skills_editor_inspect",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_Inspect(
        [Description("Name of the skill folder under /skills.")] string skillName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var manifestPath = $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}/{OneDriveOpenSkills.SkillManifestName}";
        var manifestText = await graph.ReadTextFileAsync(drive.Id!, manifestPath, cancellationToken)
                           ?? throw new ValidationException($"Skill '{normalizedName}' is missing SKILL.md.");

        var parsed = SkillDocumentParser.Parse(manifestText, normalizedName);

        return new SkillInspectionResult
        {
            SkillName = normalizedName,
            IsValid = parsed.Errors.Count == 0,
            Name = parsed.Name,
            Description = parsed.Description,
            License = parsed.License,
            Compatibility = parsed.Compatibility,
            AllowedTools = parsed.AllowedTools,
            Metadata = parsed.Metadata,
            Warnings = parsed.Warnings,
            Errors = parsed.Errors,
            Body = parsed.Body
        };
    })));

    [Description("Create a new spec-compliant skill under /skills in the user's OneDrive.")]
    [McpServerTool(
        Title = "Create OneDrive Skill",
        Name = "onedrive_skills_editor_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_Create(
        [Description("Skill name. Lowercase letters, numbers, and hyphens only.")] string? name,
        [Description("Skill description. Required by the Agent Skills specification.")] string? description,
        [Description("Optional markdown instructions body.")] string? instructions,
        [Description("Optional license field.")] string? license,
        [Description("Optional compatibility field.")] string? compatibility,
        [Description("Optional allowed-tools field.")] string? allowedTools,
        [Description("Optional metadata lines in key=value format.")] string? metadata,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new OneDriveSkillCreateInput
            {
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
                Instructions = instructions,
                License = license,
                Compatibility = compatibility,
                AllowedTools = allowedTools,
                Metadata = metadata
            },
            cancellationToken);

        ArgumentNullException.ThrowIfNull(typed);

        var definition = BuildDefinition(typed);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        await graph.EnsureFolderExistsAsync(drive.Id!, OneDriveOpenSkills.RootFolderName, cancellationToken);

        var folderPath = $"/{OneDriveOpenSkills.RootFolderName}/{definition.Name}";
        var manifestPath = $"{folderPath}/{OneDriveOpenSkills.SkillManifestName}";

        var existing = await graph.ReadTextFileAsync(drive.Id!, manifestPath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
            throw new ValidationException($"Skill '{definition.Name}' already exists.");

        await graph.EnsureFolderExistsAsync(drive.Id!, folderPath, cancellationToken);
        var manifest = SkillDocumentParser.Render(definition);
        await graph.WriteTextFileAsync(drive.Id!, manifestPath, manifest, cancellationToken);

        return new SkillMutationResult
        {
            Success = true,
            SkillName = definition.Name,
            Message = "Skill created successfully.",
            Files = [OneDriveOpenSkills.SkillManifestName],
            Warnings = definition.Warnings
        };
    })));

    [Description("Create or update a text file inside a OneDrive skill folder. Uses elicitation so the client can confirm the final path and content before writing.")]
    [McpServerTool(
        Title = "Upsert OneDrive Skill File",
        Name = "onedrive_skills_editor_upsert_file",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_UpsertFile(
        [Description("Name of the skill folder under /skills.")] string skillName,
        [Description("Relative path inside the skill folder.")] string? relativePath,
        [Description("File content to write.")] string? content,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new OneDriveSkillFileUpsertInput
            {
                RelativePath = relativePath ?? string.Empty,
                Content = content ?? string.Empty
            },
            cancellationToken);

        if (notAccepted is not null)
            return notAccepted;

        ArgumentNullException.ThrowIfNull(typed);

        var normalizedPath = OneDriveOpenSkills.NormalizeRelativePath(typed.RelativePath);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        if (normalizedPath.Equals(OneDriveOpenSkills.SkillManifestName, StringComparison.OrdinalIgnoreCase))
        {
            var parsed = SkillDocumentParser.Parse(typed.Content, normalizedName);
            if (parsed.Errors.Count > 0)
                throw new ValidationException(string.Join(" ", parsed.Errors));
        }

        var folderPath = $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}";
        var targetPath = $"{folderPath}/{normalizedPath}";
        await graph.EnsureFolderExistsAsync(drive.Id!, Path.GetDirectoryName(targetPath.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/') ?? folderPath, cancellationToken);
        await graph.WriteTextFileAsync(drive.Id!, targetPath, typed.Content, cancellationToken);

        return new SkillMutationResult
        {
            Success = true,
            SkillName = normalizedName,
            Message = $"File '{normalizedPath}' saved successfully.",
            Files = [normalizedPath]
        }.ToJsonContentBlock($"onedrive://skills/{normalizedName}/{normalizedPath}").ToCallToolResult();
    }));

    [Description("Update the SKILL.md manifest in a structured, spec-aware way. Uses elicitation so the client can confirm all metadata before writing.")]
    [McpServerTool(
        Title = "Update OneDrive Skill Manifest",
        Name = "onedrive_skills_editor_update_manifest",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_UpdateManifest(
        [Description("Name of the skill folder under /skills.")] string skillName,
        [Description("Updated description.")] string? description,
        [Description("Updated instructions body.")] string? instructions,
        [Description("Optional updated license field.")] string? license,
        [Description("Optional updated compatibility field.")] string? compatibility,
        [Description("Optional updated allowed-tools field.")] string? allowedTools,
        [Description("Optional updated metadata lines in key=value format.")] string? metadata,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        var manifestPath = $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}/{OneDriveOpenSkills.SkillManifestName}";
        var existing = await graph.ReadTextFileAsync(drive.Id!, manifestPath, cancellationToken)
                       ?? throw new ValidationException($"Skill '{normalizedName}' is missing SKILL.md.");

        var parsed = SkillDocumentParser.Parse(existing, normalizedName);
        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new OneDriveSkillManifestInput
            {
                Name = normalizedName,
                Description = description ?? parsed.Description ?? string.Empty,
                Instructions = instructions ?? parsed.Body,
                License = license ?? parsed.License,
                Compatibility = compatibility ?? parsed.Compatibility,
                AllowedTools = allowedTools ?? parsed.AllowedTools,
                Metadata = metadata ?? SkillDocumentParser.RenderMetadataLines(parsed.Metadata)
            },
            cancellationToken);

        ArgumentNullException.ThrowIfNull(typed);
        typed.Name = normalizedName;

        var definition = BuildDefinition(typed);
        var manifest = SkillDocumentParser.Render(definition);
        await graph.WriteTextFileAsync(drive.Id!, manifestPath, manifest, cancellationToken);

        return new SkillMutationResult
        {
            Success = true,
            SkillName = normalizedName,
            Message = "SKILL.md updated successfully.",
            Files = [OneDriveOpenSkills.SkillManifestName],
            Warnings = definition.Warnings
        };
    })));

    [Description("Import a single text file into an existing OneDrive skill folder from a direct URL.")]
    [McpServerTool(
        Title = "Import OneDrive Skill File",
        Name = "onedrive_skills_editor_import_file",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_ImportFile(
        [Description("Name of the skill folder under /skills.")] string skillName,
        [Description("Direct file URL to download.")] string? fileUrl,
        [Description("Destination relative path inside the skill folder.")] string? relativePath,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default) =>
        await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
        await context.WithStructuredContent(async () =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var (typed, notAccepted, _) = await context.Server.TryElicit(
            new OneDriveSkillImportInput
            {
                FileUrl = fileUrl ?? string.Empty,
                RelativePath = relativePath ?? string.Empty
            },
            cancellationToken);

        ArgumentNullException.ThrowIfNull(typed);

        var normalizedPath = OneDriveOpenSkills.NormalizeRelativePath(typed.RelativePath);
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var downloaded = await downloadService.DownloadContentAsync(serviceProvider, context.Server, typed.FileUrl, cancellationToken);
        var file = downloaded.FirstOrDefault() ?? throw new ValidationException("Could not download the requested file.");

        var content = file.Contents.ToString();
        if (normalizedPath.Equals(OneDriveOpenSkills.SkillManifestName, StringComparison.OrdinalIgnoreCase))
        {
            var manifest = SkillDocumentParser.Parse(content, normalizedName);
            if (manifest.Errors.Count > 0)
                throw new ValidationException(string.Join(" ", manifest.Errors));
        }

        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");
        var folderPath = $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}";
        var targetPath = $"{folderPath}/{normalizedPath}";
        await graph.EnsureFolderExistsAsync(drive.Id!, Path.GetDirectoryName(targetPath.Replace('/', Path.DirectorySeparatorChar))?.Replace(Path.DirectorySeparatorChar, '/') ?? folderPath, cancellationToken);
        await graph.WriteTextFileAsync(drive.Id!, targetPath, content, cancellationToken);

        return new SkillMutationResult
        {
            Success = true,
            SkillName = normalizedName,
            Message = $"Imported '{file.Filename ?? file.Uri}' into '{normalizedPath}'.",
            Files = [normalizedPath]
        };
    })));

    [Description("Delete an entire OneDrive skill folder after explicit confirmation.")]
    [McpServerTool(
        Title = "Delete OneDrive Skill",
        Name = "onedrive_skills_editor_delete_skill",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OneDriveSkillsEditor_DeleteSkill(
        [Description("Name of the skill folder under /skills.")] string skillName,
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken = default)
        => await context.WithExceptionCheck(async () =>
        await context.WithOboGraphClient(async graph =>
    {
        var normalizedName = OneDriveOpenSkills.NormalizeSkillName(skillName);
        var drive = await graph.GetDefaultDriveAsync(cancellationToken)
                   ?? throw new Exception("Could not resolve default OneDrive.");

        return await context.ConfirmAndDeleteAsync<OneDriveDeleteSkillConfirmation>(
            expectedName: normalizedName,
            deleteAction: async _ =>
            {
                await graph.DeleteFolderIfExistsAsync(drive.Id!, $"/{OneDriveOpenSkills.RootFolderName}/{normalizedName}", cancellationToken);
            },
            successText: $"OneDrive skill '{normalizedName}' deleted successfully!",
            ct: cancellationToken);
    }));

    private static SkillWriteDefinition BuildDefinition(OneDriveSkillCreateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = ValidateSpecSkillName(input.Name);
        var description = ValidateDescription(input.Description);
        var metadata = SkillDocumentParser.ParseMetadataLines(input.Metadata);
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(input.Compatibility) && input.Compatibility.Trim().Length > 500)
            warnings.Add("compatibility exceeds the recommended 500 character limit.");

        return new SkillWriteDefinition
        {
            Name = name,
            Description = description,
            License = string.IsNullOrWhiteSpace(input.License) ? null : input.License.Trim(),
            Compatibility = string.IsNullOrWhiteSpace(input.Compatibility) ? null : input.Compatibility.Trim(),
            AllowedTools = string.IsNullOrWhiteSpace(input.AllowedTools) ? null : input.AllowedTools.Trim(),
            Metadata = metadata,
            Body = string.IsNullOrWhiteSpace(input.Instructions)
                ? "# " + name + "\n\n## When to use this skill\nDescribe when this skill should be activated.\n\n## Instructions\nAdd the exact workflow the agent should follow.\n"
                : input.Instructions.Trim(),
            Warnings = warnings
        };
    }

    private static string ValidateSpecSkillName(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new ValidationException("name is required.");

        var normalized = OneDriveOpenSkills.NormalizeSkillName(skillName);
        if (normalized.Length > 64)
            throw new ValidationException("name must be 64 characters or less.");

        if (!Regex.IsMatch(normalized, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            throw new ValidationException("name must contain only lowercase letters, numbers, and single hyphens, and it must not start or end with a hyphen.");

        return normalized;
    }

    private static string ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("description is required.");

        var normalized = description.Trim();
        if (normalized.Length > 1024)
            throw new ValidationException("description must be 1024 characters or less.");

        return normalized;
    }

    private sealed class SkillMutationResult
    {
        public bool Success { get; set; }
        public string SkillName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string>? Files { get; set; }
        public List<string>? Warnings { get; set; }
    }

    private sealed class SkillInspectionResult
    {
        public string SkillName { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? License { get; set; }
        public string? Compatibility { get; set; }
        public string? AllowedTools { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; set; } = [];
        public List<string> Errors { get; set; } = [];
        public string Body { get; set; } = string.Empty;
    }

    internal sealed class SkillWriteDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? License { get; set; }
        public string? Compatibility { get; set; }
        public string? AllowedTools { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = [];
    }
}

internal static class SkillDocumentParser
{
    public static ParsedSkillDocument Parse(string text, string expectedDirectoryName)
    {
        var result = new ParsedSkillDocument();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Errors.Add("SKILL.md is empty.");
            return result;
        }

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var bodyStart = 0;
        var frontmatter = new List<string>();

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var closed = false;
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    bodyStart = i + 1;
                    closed = true;
                    break;
                }

                frontmatter.Add(lines[i]);
            }

            if (!closed)
                result.Errors.Add("SKILL.md frontmatter is missing a closing '---' line.");
        }
        else
        {
            result.Warnings.Add("SKILL.md does not start with YAML frontmatter. This is allowed for legacy reads but not for strict spec compliance.");
            result.Body = normalized.Trim();
            result.Errors.Add("SKILL.md must start with YAML frontmatter.");
            return result;
        }

        ParseFrontmatter(result, frontmatter);
        result.Body = string.Join("\n", lines.Skip(bodyStart)).Trim();

        if (string.IsNullOrWhiteSpace(result.Name))
            result.Errors.Add("Frontmatter must contain a non-empty name field.");
        else if (!string.Equals(result.Name, expectedDirectoryName, StringComparison.Ordinal))
            result.Errors.Add($"Frontmatter name '{result.Name}' must match the folder name '{expectedDirectoryName}'.");

        if (string.IsNullOrWhiteSpace(result.Description))
            result.Errors.Add("Frontmatter must contain a non-empty description field.");

        if (!string.IsNullOrWhiteSpace(result.Name))
        {
            if (result.Name.Length > 64)
                result.Errors.Add("name must be 64 characters or less.");
            if (!Regex.IsMatch(result.Name, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
                result.Errors.Add("name must contain only lowercase letters, numbers, and single hyphens.");
        }

        if (!string.IsNullOrWhiteSpace(result.Description) && result.Description.Length > 1024)
            result.Errors.Add("description must be 1024 characters or less.");

        if (!string.IsNullOrWhiteSpace(result.Compatibility) && result.Compatibility.Length > 500)
            result.Warnings.Add("compatibility exceeds the recommended 500 character limit.");

        return result;
    }

    public static string Render(OneDriveSkillsEditor.SkillWriteDefinition definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {QuoteYaml(definition.Name)}");
        sb.AppendLine($"description: {QuoteYaml(definition.Description)}");

        if (!string.IsNullOrWhiteSpace(definition.License))
            sb.AppendLine($"license: {QuoteYaml(definition.License)}");
        if (!string.IsNullOrWhiteSpace(definition.Compatibility))
            sb.AppendLine($"compatibility: {QuoteYaml(definition.Compatibility)}");
        if (!string.IsNullOrWhiteSpace(definition.AllowedTools))
            sb.AppendLine($"allowed-tools: {QuoteYaml(definition.AllowedTools)}");

        if (definition.Metadata.Count > 0)
        {
            sb.AppendLine("metadata:");
            foreach (var item in definition.Metadata.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"  {item.Key}: {QuoteYaml(item.Value)}");
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(definition.Body.Replace("\r\n", "\n").Trim());
        sb.AppendLine();
        return sb.ToString();
    }

    public static Dictionary<string, string> ParseMetadataLines(string? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(metadata))
            return result;

        foreach (var rawLine in metadata.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new ValidationException("metadata lines must use key=value format.");

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                throw new ValidationException("metadata keys cannot be empty.");

            result[key] = value;
        }

        return result;
    }

    public static string RenderMetadataLines(Dictionary<string, string> metadata)
        => string.Join("\n", metadata.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(a => $"{a.Key}={a.Value}"));

    private static void ParseFrontmatter(ParsedSkillDocument result, List<string> lines)
    {
        var currentKey = string.Empty;
        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("  ") && currentKey.Equals("metadata", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = line.Trim();
                var idx = trimmed.IndexOf(':');
                if (idx > 0)
                {
                    var key = trimmed[..idx].Trim();
                    var metadataValue = Unquote(trimmed[(idx + 1)..].Trim());
                    if (!string.IsNullOrWhiteSpace(key))
                        result.Metadata[key] = metadataValue;
                }

                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var keyName = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            currentKey = keyName;

            switch (keyName)
            {
                case "name":
                    result.Name = value;
                    break;
                case "description":
                    result.Description = value;
                    break;
                case "license":
                    result.License = value;
                    break;
                case "compatibility":
                    result.Compatibility = value;
                    break;
                case "allowed-tools":
                    result.AllowedTools = value;
                    break;
                case "metadata":
                    break;
                default:
                    result.Metadata[keyName] = value;
                    result.Warnings.Add($"Unrecognized frontmatter field '{keyName}' was preserved as metadata for compatibility.");
                    break;
            }
        }
    }

    private static string Unquote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
            return trimmed[1..^1];

        return trimmed;
    }

    private static string QuoteYaml(string value)
        => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    internal sealed class ParsedSkillDocument
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? License { get; set; }
        public string? Compatibility { get; set; }
        public string? AllowedTools { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = [];
        public List<string> Errors { get; set; } = [];
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

    public static async Task<List<string>> ListFilesRecursivelyIfExistsAsync(
        this GraphServiceClient graph,
        string driveId,
        string folderPath,
        CancellationToken ct)
    {
        try
        {
            var root = await graph.Drives[driveId]
                .Root
                .ItemWithPath(folderPath.Trim('/'))
                .GetAsync(cancellationToken: ct);

            if (root?.Id is null)
                return [];

            var results = new List<string>();
            await CollectFilesRecursiveAsync(graph, driveId, root.Id, string.Empty, results, ct);
            return results.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static async Task DeleteFolderIfExistsAsync(
        this GraphServiceClient graph,
        string driveId,
        string folderPath,
        CancellationToken ct)
    {
        try
        {
            await graph.Drives[driveId]
                .Root
                .ItemWithPath(folderPath.Trim('/'))
                .DeleteAsync(cancellationToken: ct);
        }
        catch
        {
            // ignore when already absent
        }
    }

    private static async Task CollectFilesRecursiveAsync(
        GraphServiceClient graph,
        string driveId,
        string itemId,
        string relativePrefix,
        List<string> sink,
        CancellationToken ct)
    {
        var page = await graph.Drives[driveId].Items[itemId].Children.GetAsync(cancellationToken: ct);

        while (true)
        {
            foreach (var child in page?.Value ?? Enumerable.Empty<DriveItem>())
            {
                ct.ThrowIfCancellationRequested();

                var childName = child.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(childName))
                    continue;

                var childPath = string.IsNullOrWhiteSpace(relativePrefix)
                    ? childName
                    : $"{relativePrefix}/{childName}";

                if (child.Folder != null)
                {
                    await CollectFilesRecursiveAsync(graph, driveId, child.Id!, childPath, sink, ct);
                    continue;
                }

                sink.Add(childPath);
            }

            var nextLink = page?.OdataNextLink;
            if (string.IsNullOrWhiteSpace(nextLink))
                break;

            page = await graph.Drives[driveId].Items[itemId].Children.WithUrl(nextLink!).GetAsync(cancellationToken: ct);
        }
    }
}
