using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using MCPhappey.Tools.OneDrive.OpenSkills;

namespace MCPhappey.Tools.OneDrive.AgentPlugins;

internal sealed record AgentSkillSnapshot(
    string Name,
    string Description,
    IReadOnlyList<AgentPluginBinaryFile> Files,
    IReadOnlyList<string> Warnings)
{
    public static AgentSkillSnapshot FromSharedFolder(
        string folderName,
        IReadOnlyList<AgentPluginBinaryFile> files)
        => Validate(folderName, files);

    public static AgentSkillSnapshot FromZip(ReadOnlyMemory<byte> zipBytes)
    {
        using var input = new MemoryStream(zipBytes.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).ToArray();
        if (entries.Length == 0)
            throw new ValidationException("Skill ZIP is empty.");
        if (entries.Length > OneDriveAgentPluginStorage.MaximumEntries)
            throw new ValidationException("Skill ZIP contains too many files.");
        if (entries.Sum(entry => entry.Length) > OneDriveAgentPluginStorage.MaximumExpandedBytes)
            throw new ValidationException("Skill ZIP exceeds the expanded size limit.");

        var normalizedEntries = entries.Select(entry =>
        {
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
                throw new ValidationException($"Skill ZIP symlink '{entry.FullName}' is not allowed.");
            return (Entry: entry, Path: AgentPluginSpecification.NormalizePackagePath(entry.FullName));
        }).ToArray();

        var duplicate = normalizedEntries.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ValidationException($"Skill ZIP contains duplicate path '{duplicate.Key}'.");

        string rootName;
        string rootPrefix;
        if (normalizedEntries.Any(item => item.Path == "SKILL.md"))
        {
            rootName = ReadSkillName(normalizedEntries.Single(item => item.Path == "SKILL.md").Entry);
            rootPrefix = string.Empty;
        }
        else
        {
            var candidates = normalizedEntries
                .Where(item => item.Path.EndsWith("/SKILL.md", StringComparison.Ordinal))
                .Select(item => item.Path[..^"/SKILL.md".Length])
                .Where(prefix => !prefix.Contains('/'))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length != 1)
                throw new ValidationException("Skill ZIP must contain exactly one root skill directory with SKILL.md.");
            rootName = candidates[0];
            rootPrefix = rootName + "/";
            if (normalizedEntries.Any(item => !item.Path.StartsWith(rootPrefix, StringComparison.Ordinal)))
                throw new ValidationException("Every Skill ZIP file must remain inside its single root skill directory.");
        }

        var files = new List<AgentPluginBinaryFile>();
        foreach (var item in normalizedEntries)
        {
            var relativePath = string.IsNullOrEmpty(rootPrefix) ? item.Path : item.Path[rootPrefix.Length..];
            using var entryStream = item.Entry.Open();
            using var output = new MemoryStream();
            entryStream.CopyTo(output);
            files.Add(new(relativePath, output.ToArray()));
        }
        return Validate(rootName, files);
    }

    private static AgentSkillSnapshot Validate(string folderName, IReadOnlyList<AgentPluginBinaryFile> files)
    {
        if (string.IsNullOrWhiteSpace(folderName) || folderName.Contains('/') || folderName.Contains('\\'))
            throw new ValidationException("Skill source folder must have a single valid folder name.");
        if (files.Count == 0)
            throw new ValidationException("Skill source folder is empty.");
        if (files.Count > OneDriveAgentPluginStorage.MaximumEntries
            || files.Sum(file => (long)file.Bytes.Length) > OneDriveAgentPluginStorage.MaximumExpandedBytes)
            throw new ValidationException("Skill source exceeds package safety limits.");

        var normalized = files.Select(file => file with
        {
            Path = AgentPluginSpecification.NormalizePackagePath(file.Path)
        }).ToArray();
        var duplicate = normalized.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ValidationException($"Skill source contains duplicate path '{duplicate.Key}'.");

        var skillMarkdown = normalized.SingleOrDefault(file => file.Path == "SKILL.md")
            ?? throw new ValidationException("Skill source must contain SKILL.md at its root.");
        var parsed = SkillDocumentParser.Parse(Encoding.UTF8.GetString(skillMarkdown.Bytes), folderName);
        if (parsed.Errors.Count > 0)
            throw new ValidationException(string.Join(" ", parsed.Errors));
        if (string.IsNullOrWhiteSpace(parsed.Name) || string.IsNullOrWhiteSpace(parsed.Description))
            throw new ValidationException("SKILL.md must define name and description.");

        return new(parsed.Name, parsed.Description, normalized, parsed.Warnings);
    }

    private static string ReadSkillName(ZipArchiveEntry skillMarkdown)
    {
        using var stream = skillMarkdown.Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        var parsed = SkillDocumentParser.Parse(Encoding.UTF8.GetString(output.ToArray()), string.Empty);
        if (string.IsNullOrWhiteSpace(parsed.Name))
            throw new ValidationException("Root SKILL.md must define a valid name.");
        return parsed.Name;
    }
}
