using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Azure;

public sealed class SkillsStorageSettings
{
    public string? ConnectionString { get; set; }

    public string? BlobContainerName { get; set; }

    [JsonIgnore]
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(ConnectionString)
           && !string.IsNullOrWhiteSpace(BlobContainerName);
}

internal sealed class AzureSkillsStorageService(SkillsStorageSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private BlobContainerClient? _containerClient;

    public bool IsConfigured => settings.IsConfigured;

    public async Task<AzureSkillListResponse> SearchSkillsAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return AzureSkillListResponse.Empty("SkillsStorage is not configured.");

        var container = GetContainerClient();

        if (!await ContainerExistsAsync(container, cancellationToken))
            return AzureSkillListResponse.Empty();

        var allSkillNames = await ListSkillNamesAsync(container, cancellationToken);
        var skillRecords = new List<AzureSkillListItem>();

        foreach (var skillName in allSkillNames)
        {
            var summary = await GetSkillSummaryAsync(skillName, cancellationToken);
            if (summary is null)
                continue;

            if (!Matches(summary, query))
                continue;

            skillRecords.Add(summary);
        }

        var ordered = skillRecords
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();

        return AzureSkillListResponse.FromItems(ordered);
    }

    public async Task<AzureSkillActivationResult> ActivateSkillAsync(string skillName, string? version, CancellationToken cancellationToken)
    {
        ValidateSkillName(skillName);

        var resolvedVersion = await ResolveRequestedVersionAsync(skillName, version, cancellationToken)
            ?? throw new InvalidOperationException($"Skill '{skillName}' was not found.");

        var skillFile = await ReadSkillFileAsync(skillName, resolvedVersion, "SKILL.md", cancellationToken)
            ?? throw new InvalidOperationException($"Skill '{skillName}' version '{resolvedVersion}' does not contain SKILL.md.");

        var files = await ListFilesForVersionAsync(skillName, resolvedVersion, cancellationToken);

        return new AzureSkillActivationResult
        {
            SkillName = skillName,
            Version = resolvedVersion,
            RelativePath = skillFile.RelativePath,
            Content = skillFile.TextContent,
            MimeType = skillFile.ContentType,
            AvailableFiles = files
        };
    }

    public async Task<AzureSkillFileReadResult> ReadSkillFileStrictAsync(string skillName, string? version, string relativePath, CancellationToken cancellationToken)
    {
        ValidateSkillName(skillName);
        var normalizedPath = NormalizeRelativePath(relativePath);

        var resolvedVersion = await ResolveRequestedVersionAsync(skillName, version, cancellationToken)
            ?? throw new InvalidOperationException($"Skill '{skillName}' was not found.");

        var file = await ReadSkillFileAsync(skillName, resolvedVersion, normalizedPath, cancellationToken)
            ?? throw new InvalidOperationException($"File '{normalizedPath}' was not found in skill '{skillName}' version '{resolvedVersion}'.");

        return file;
    }

    public async Task<AzureSkillMutationResult> CreateSkillAsync(IEnumerable<AzureSkillUploadFile> files, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var prepared = PrepareUploadedFiles(files);
        var skillDefinition = GetSkillDefinition(prepared);
        var skillName = skillDefinition.Name;

        if (await SkillExistsAsync(skillName, cancellationToken))
            throw new ValidationException($"Skill '{skillName}' already exists. Create a new version instead.");

        var version = "1";
        await EnsureContainerExistsAsync(cancellationToken);
        await UploadVersionFilesAsync(skillName, version, prepared, cancellationToken);

        var metadata = new AzureSkillMetadata
        {
            Id = skillName,
            Name = skillName,
            Description = skillDefinition.Description,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DefaultVersion = version,
            LatestVersion = version,
            Object = "skill"
        };

        await WriteMetadataAsync(skillName, metadata, cancellationToken);

        return AzureSkillMutationResult.Created(skillName, version, metadata.DefaultVersion, metadata.LatestVersion, prepared.Select(a => a.RelativePath).ToList());
    }

    public async Task<AzureSkillMutationResult> CreateVersionAsync(string skillName, IEnumerable<AzureSkillUploadFile> files, bool makeDefault, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateSkillName(skillName);

        var prepared = PrepareUploadedFiles(files);
        var skillDefinition = GetSkillDefinition(prepared);

        if (!skillDefinition.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"SKILL.md name '{skillDefinition.Name}' must match the requested skill name '{skillName}'.");

        var existingVersions = await ListVersionsAsync(skillName, cancellationToken);
        if (existingVersions.Count == 0)
            throw new InvalidOperationException($"Skill '{skillName}' does not exist yet. Create the skill first.");

        var nextVersion = GetNextVersion(existingVersions);

        await EnsureContainerExistsAsync(cancellationToken);
        await UploadVersionFilesAsync(skillName, nextVersion, prepared, cancellationToken);

        var metadata = await ReadMetadataAsync(skillName, cancellationToken)
            ?? new AzureSkillMetadata
            {
                Id = skillName,
                Name = skillName,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Object = "skill"
            };

        metadata.Name = skillName;
        metadata.Description = skillDefinition.Description;
        metadata.LatestVersion = nextVersion;
        metadata.DefaultVersion ??= existingVersions.OrderByDescending(ParseVersionForSort).FirstOrDefault() ?? nextVersion;

        if (makeDefault)
            metadata.DefaultVersion = nextVersion;

        await WriteMetadataAsync(skillName, metadata, cancellationToken);

        return AzureSkillMutationResult.Created(skillName, nextVersion, metadata.DefaultVersion, metadata.LatestVersion, prepared.Select(a => a.RelativePath).ToList());
    }

    public async Task<AzureSkillMutationResult> SetDefaultVersionAsync(string skillName, string defaultVersion, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateSkillName(skillName);
        ValidateVersion(defaultVersion);

        var versions = await ListVersionsAsync(skillName, cancellationToken);
        if (!versions.Contains(defaultVersion, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Version '{defaultVersion}' was not found for skill '{skillName}'.");

        var metadata = await ReadMetadataAsync(skillName, cancellationToken)
            ?? throw new InvalidOperationException($"Skill '{skillName}' metadata was not found.");

        metadata.DefaultVersion = defaultVersion;
        metadata.LatestVersion ??= versions.OrderByDescending(ParseVersionForSort).FirstOrDefault();
        await WriteMetadataAsync(skillName, metadata, cancellationToken);

        return AzureSkillMutationResult.Updated(skillName, defaultVersion, metadata.DefaultVersion, metadata.LatestVersion, "Default version updated.");
    }

    public async Task DeleteSkillAsync(string skillName, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateSkillName(skillName);

        var container = GetContainerClient();
        await foreach (var blob in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: $"{skillName}/", cancellationToken: cancellationToken))
        {
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }

    public async Task DeleteVersionAsync(string skillName, string version, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        ValidateSkillName(skillName);
        ValidateVersion(version);

        var container = GetContainerClient();
        var prefix = BuildVersionPrefix(skillName, version);
        var found = false;

        await foreach (var blob in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: prefix, cancellationToken: cancellationToken))
        {
            found = true;
            await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }

        if (!found)
            throw new InvalidOperationException($"Version '{version}' was not found for skill '{skillName}'.");

        var remainingVersions = await ListVersionsAsync(skillName, cancellationToken);
        if (remainingVersions.Count == 0)
        {
            await DeleteSkillAsync(skillName, cancellationToken);
            return;
        }

        var metadata = await ReadMetadataAsync(skillName, cancellationToken)
            ?? new AzureSkillMetadata
            {
                Id = skillName,
                Name = skillName,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Object = "skill"
            };

        var latestVersion = remainingVersions.OrderByDescending(ParseVersionForSort).First();
        metadata.LatestVersion = latestVersion;

        if (string.Equals(metadata.DefaultVersion, version, StringComparison.OrdinalIgnoreCase))
            metadata.DefaultVersion = latestVersion;

        await WriteMetadataAsync(skillName, metadata, cancellationToken);
    }

    private BlobContainerClient GetContainerClient()
        => _containerClient ??= new BlobContainerClient(settings.ConnectionString!, settings.BlobContainerName!);

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SkillsStorage is not configured.");
    }

    private async Task<bool> SkillExistsAsync(string skillName, CancellationToken cancellationToken)
    {
        var versions = await ListVersionsAsync(skillName, cancellationToken);
        return versions.Count > 0;
    }

    private async Task<bool> ContainerExistsAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        var exists = await container.ExistsAsync(cancellationToken);
        return exists.Value;
    }

    private async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
        => await GetContainerClient().CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    private async Task<List<string>> ListSkillNamesAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        var skillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            var segments = blob.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 2)
                continue;

            if (segments[0].StartsWith(".", StringComparison.Ordinal))
                continue;

            skillNames.Add(segments[0]);
        }

        return skillNames.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<string>> ListVersionsAsync(string skillName, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return [];

        var container = GetContainerClient();
        if (!await ContainerExistsAsync(container, cancellationToken))
            return [];

        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var blob in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: $"{skillName}/", cancellationToken: cancellationToken))
        {
            var segments = blob.Name.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length < 3)
                continue;

            var version = segments[1];
            if (version.StartsWith(".", StringComparison.Ordinal))
                continue;

            versions.Add(version);
        }

        return versions
            .OrderByDescending(ParseVersionForSort)
            .ThenByDescending(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string?> ResolveRequestedVersionAsync(string skillName, string? version, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SkillsStorage is not configured.");

        if (!string.IsNullOrWhiteSpace(version))
        {
            ValidateVersion(version);
            return version.Trim();
        }

        var metadata = await ReadMetadataAsync(skillName, cancellationToken);
        if (!string.IsNullOrWhiteSpace(metadata?.LatestVersion))
            return metadata!.LatestVersion;

        var versions = await ListVersionsAsync(skillName, cancellationToken);
        return versions.FirstOrDefault();
    }

    private async Task<AzureSkillListItem?> GetSkillSummaryAsync(string skillName, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadataAsync(skillName, cancellationToken);
        var versions = await ListVersionsAsync(skillName, cancellationToken);
        if (versions.Count == 0)
            return null;

        var latestVersion = metadata?.LatestVersion;
        if (string.IsNullOrWhiteSpace(latestVersion) || !versions.Contains(latestVersion, StringComparer.OrdinalIgnoreCase))
            latestVersion = versions.First();

        var defaultVersion = metadata?.DefaultVersion;
        if (string.IsNullOrWhiteSpace(defaultVersion) || !versions.Contains(defaultVersion, StringComparer.OrdinalIgnoreCase))
            defaultVersion = latestVersion;

        var description = metadata?.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            var skillFile = await ReadSkillFileAsync(skillName, latestVersion, "SKILL.md", cancellationToken);
            description = skillFile is null ? null : ParseSkillDefinition(skillFile.TextContent).Description;
        }

        return new AzureSkillListItem
        {
            Id = skillName,
            Name = skillName,
            Description = description ?? string.Empty,
            CreatedAt = metadata?.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DefaultVersion = defaultVersion ?? latestVersion!,
            LatestVersion = latestVersion!,
            Object = "skill"
        };
    }

    private static bool Matches(AzureSkillListItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AzureSkillMetadata?> ReadMetadataAsync(string skillName, CancellationToken cancellationToken)
    {
        var container = GetContainerClient();
        if (!await ContainerExistsAsync(container, cancellationToken))
            return null;

        var blob = container.GetBlobClient(BuildMetadataBlobName(skillName));
        var exists = await blob.ExistsAsync(cancellationToken);
        if (!exists.Value)
            return null;

        var content = await blob.DownloadContentAsync(cancellationToken);
        return content.Value.Content.ToObjectFromJson<AzureSkillMetadata>(JsonOptions);
    }

    private async Task WriteMetadataAsync(string skillName, AzureSkillMetadata metadata, CancellationToken cancellationToken)
    {
        var container = GetContainerClient();
        var blob = container.GetBlobClient(BuildMetadataBlobName(skillName));
        await blob.UploadAsync(BinaryData.FromObjectAsJson(metadata, JsonOptions), overwrite: true, cancellationToken);
    }

    private async Task UploadVersionFilesAsync(string skillName, string version, IReadOnlyList<AzureSkillUploadFile> files, CancellationToken cancellationToken)
    {
        var container = GetContainerClient();

        foreach (var file in files)
        {
            var blob = container.GetBlobClient($"{BuildVersionPrefix(skillName, version)}{file.RelativePath}");
            await blob.UploadAsync(file.Contents, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType
                }
            }, cancellationToken);
        }
    }

    private async Task<List<string>> ListFilesForVersionAsync(string skillName, string version, CancellationToken cancellationToken)
    {
        var container = GetContainerClient();
        var files = new List<string>();
        var prefix = BuildVersionPrefix(skillName, version);

        await foreach (var blob in container.GetBlobsAsync(
            traits: BlobTraits.None,
            states: BlobStates.None,
            prefix: prefix, cancellationToken: cancellationToken))
        {
            var relativePath = blob.Name[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            if (relativePath.StartsWith(".", StringComparison.Ordinal))
                continue;

            files.Add(relativePath);
        }

        return files.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<AzureSkillFileReadResult?> ReadSkillFileAsync(string skillName, string version, string relativePath, CancellationToken cancellationToken)
    {
        var container = GetContainerClient();
        if (!await ContainerExistsAsync(container, cancellationToken))
            return null;

        var normalizedPath = NormalizeRelativePath(relativePath);
        var blob = container.GetBlobClient($"{BuildVersionPrefix(skillName, version)}{normalizedPath}");
        var exists = await blob.ExistsAsync(cancellationToken);
        if (!exists.Value)
            return null;

        var download = await blob.DownloadContentAsync(cancellationToken);
        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
        var contentType = properties.Value.ContentType ?? GuessContentType(normalizedPath);
        var bytes = download.Value.Content.ToArray();
        var isText = IsTextContentType(contentType);

        return new AzureSkillFileReadResult
        {
            SkillName = skillName,
            Version = version,
            RelativePath = normalizedPath,
            MimeType = contentType,
            IsText = isText,
            Content = isText ? Encoding.UTF8.GetString(bytes) : null,
            ContentBase64 = isText ? null : Convert.ToBase64String(bytes)
        };
    }

    private static IReadOnlyList<AzureSkillUploadFile> PrepareUploadedFiles(IEnumerable<AzureSkillUploadFile> files)
    {
        var normalized = files
            .Select(a => new AzureSkillUploadFile(NormalizeRelativePath(a.RelativePath), a.Contents, string.IsNullOrWhiteSpace(a.ContentType) ? GuessContentType(a.RelativePath) : a.ContentType))
            .Where(a => !string.IsNullOrWhiteSpace(a.RelativePath))
            .ToList();

        if (normalized.Count == 0)
            throw new ValidationException("No skill files were provided.");

        normalized = StripSharedRootFolder(normalized);

        if (!normalized.Any(a => a.RelativePath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("The uploaded skill must contain a SKILL.md file.");

        return normalized;
    }

    private static SkillDefinition GetSkillDefinition(IReadOnlyList<AzureSkillUploadFile> files)
    {
        var skillFile = files.FirstOrDefault(a => a.RelativePath.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException("The uploaded skill must contain a SKILL.md file.");

        return ParseSkillDefinition(skillFile.Contents.ToString());
    }

    private static SkillDefinition ParseSkillDefinition(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ValidationException("SKILL.md is empty.");

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 3 || !lines[0].Trim().Equals("---", StringComparison.Ordinal))
            throw new ValidationException("SKILL.md must start with YAML frontmatter.");

        var frontmatterLines = new List<string>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Equals("---", StringComparison.Ordinal))
                break;

            frontmatterLines.Add(lines[i]);
        }

        string? name = null;
        string? description = null;

        foreach (var line in frontmatterLines)
        {
            var index = line.IndexOf(':');
            if (index < 0)
                continue;

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim().Trim('"', '\'');

            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                name = value;
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
                description = value;
        }

        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("SKILL.md frontmatter must contain a non-empty name.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ValidationException("SKILL.md frontmatter must contain a non-empty description.");

        ValidateSkillName(name);

        return new SkillDefinition(name, description);
    }

    internal static IReadOnlyList<AzureSkillUploadFile> ExtractFilesFromZip(FileItem zipFile)
    {
        ArgumentNullException.ThrowIfNull(zipFile);

        using var stream = new MemoryStream(zipFile.Contents.ToArray(), writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var files = archive.Entries
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(entry =>
            {
                using var entryStream = entry.Open();
                using var memory = new MemoryStream();
                entryStream.CopyTo(memory);

                return new AzureSkillUploadFile(
                    entry.FullName.Replace('\\', '/'),
                    BinaryData.FromBytes(memory.ToArray()),
                    GuessContentType(entry.FullName));
            })
            .ToList();

        return PrepareUploadedFiles(files);
    }

    internal static IReadOnlyList<AzureSkillUploadFile> NormalizeUploadedFiles(IEnumerable<(string filename, BinaryData contents, string mimeType)> files)
        => PrepareUploadedFiles(files.Select(a => new AzureSkillUploadFile(a.filename, a.contents, a.mimeType)));

    private static List<AzureSkillUploadFile> StripSharedRootFolder(List<AzureSkillUploadFile> files)
    {
        var current = files;

        while (true)
        {
            var split = current
                .Select(a => a.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();

            if (split.Count == 0 || split.Any(a => a.Length < 2))
                return current;

            var firstSegment = split[0][0];
            if (split.Any(a => !a[0].Equals(firstSegment, StringComparison.OrdinalIgnoreCase)))
                return current;

            current = current
                .Select((file, index) => new AzureSkillUploadFile(string.Join('/', split[index].Skip(1)), file.Contents, file.ContentType))
                .ToList();
        }
    }

    private static string BuildVersionPrefix(string skillName, string version)
        => $"{skillName.Trim()}/{version.Trim()}/";

    private static string BuildMetadataBlobName(string skillName)
        => $"{skillName.Trim()}/.mcphappey-skill.json";

    private static void ValidateSkillName(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            throw new ValidationException("skillName is required.");

        if (skillName.Length > 64)
            throw new ValidationException("skillName must be 64 characters or less.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(skillName, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            throw new ValidationException("skillName must contain only lowercase letters, numbers, and single hyphens.");
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ValidationException("version is required.");

        if (!int.TryParse(version, out var parsed) || parsed < 1)
            throw new ValidationException("version must be a positive integer string such as 1, 2, or 3.");
    }

    private static string GetNextVersion(IReadOnlyList<string> versions)
    {
        var max = versions
            .Select(a => int.TryParse(a, out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();

        return (max + 1).ToString();
    }

    private static int ParseVersionForSort(string version)
        => int.TryParse(version, out var parsed) ? parsed : 0;

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ValidationException("relativePath is required.");

        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
            throw new ValidationException("relativePath is required.");

        if (segments.Any(a => a == "." || a == ".."))
            throw new ValidationException("relativePath must stay inside the skill directory.");

        return string.Join('/', segments);
    }

    private static bool IsTextContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType)
           && (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase)
               || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase));

    private static string GuessContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".md" => "text/markdown",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".yaml" or ".yml" => "application/yaml",
            ".py" => "text/x-python",
            ".js" or ".mjs" or ".cjs" => "application/javascript",
            ".ts" => "text/plain",
            ".sh" => "text/plain",
            ".ps1" => "text/plain",
            ".html" => "text/html",
            ".css" => "text/css",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private readonly record struct SkillDefinition(string Name, string Description);
}

internal sealed record AzureSkillUploadFile(string RelativePath, BinaryData Contents, string ContentType);

internal sealed class AzureSkillMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("default_version")]
    public string? DefaultVersion { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "skill";
}

public sealed class AzureSkillListResponse
{
    [JsonPropertyName("data")]
    public List<AzureSkillListItem> Data { get; set; } = [];

    [JsonPropertyName("first_id")]
    public string? FirstId { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public static AzureSkillListResponse FromItems(List<AzureSkillListItem> items)
        => new()
        {
            Data = items,
            FirstId = items.FirstOrDefault()?.Id,
            LastId = items.LastOrDefault()?.Id,
            HasMore = false,
            Object = "list"
        };

    public static AzureSkillListResponse Empty(string? message = null)
        => new()
        {
            Data = [],
            FirstId = null,
            LastId = null,
            HasMore = false,
            Object = "list",
            Message = message
        };
}

public sealed class AzureSkillListItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("default_version")]
    public string DefaultVersion { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "skill";
}

public sealed class AzureSkillActivationResult
{
    [JsonPropertyName("skillName")]
    public string SkillName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = "SKILL.md";

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "text/markdown";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("availableFiles")]
    public List<string> AvailableFiles { get; set; } = [];
}

public sealed class AzureSkillFileReadResult
{
    [JsonPropertyName("skillName")]
    public string SkillName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "application/octet-stream";

    [JsonPropertyName("isText")]
    public bool IsText { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("contentBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentBase64 { get; set; }

    [JsonIgnore]
    internal string TextContent => Content ?? string.Empty;

    [JsonIgnore]
    internal string ContentType => MimeType;
}

public sealed class AzureSkillMutationResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("skillName")]
    public string SkillName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    [JsonPropertyName("defaultVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultVersion { get; set; }

    [JsonPropertyName("latestVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("files")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Files { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    public static AzureSkillMutationResult Created(string skillName, string version, string? defaultVersion, string? latestVersion, List<string> files)
        => new()
        {
            Success = true,
            SkillName = skillName,
            Version = version,
            DefaultVersion = defaultVersion,
            LatestVersion = latestVersion,
            Files = files,
            Message = "Skill content uploaded successfully."
        };

    public static AzureSkillMutationResult Updated(string skillName, string? version, string? defaultVersion, string? latestVersion, string message)
        => new()
        {
            Success = true,
            SkillName = skillName,
            Version = version,
            DefaultVersion = defaultVersion,
            LatestVersion = latestVersion,
            Message = message
        };
}
