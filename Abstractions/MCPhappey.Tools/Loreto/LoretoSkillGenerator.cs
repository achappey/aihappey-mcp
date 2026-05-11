using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Loreto;

public static partial class LoretoSkillGenerator
{
    private const string ZipMimeType = "application/zip";

    [Description("Generate Agent Skill packages with Loreto from a public URL such as YouTube, article, PDF, or image URL, upload each generated .zip package with the default MCP SharePoint/OneDrive upload helper, and return resource links plus structured Loreto metadata.")]
    [McpServerTool(Title = "Generate Loreto skill packages from URL", Name = "loreto_skills_generate", ReadOnly = false, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> LoretoSkills_Generate(
        [Description("YouTube URL, article URL, public PDF URL, or public image URL.")] string source,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source type. Allowed values: auto, youtube, article, pdf, image. Default: auto.")] string sourceType = "auto",
        [Description("Test language. Allowed values: python, typescript, javascript. Default: python.")] string testLanguage = "python",
        [Description("Include Mermaid diagrams in generated SKILL.md files. Default: true.")] bool includeVisuals = true,
        [Description("Optional 1-3 sentence hint for generation, max 500 characters.")] string? context = null,
        [Description("Optional follow-up skill names to scaffold. Use comma, newline, or semicolon separated suggested_skill_name / skill_name values. Loreto accepts up to 3.")] string? themesToProcess = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ValidateEnums(sourceType, testLanguage);

            var client = serviceProvider.GetRequiredService<LoretoClient>();
            var loretoResponse = await client.GenerateAsync(
                source,
                sourceType,
                testLanguage,
                includeVisuals,
                NormalizeContext(context),
                ParseDelimited(themesToProcess, 3),
                cancellationToken);

            return await BuildPackageUploadResponseAsync(serviceProvider, requestContext, loretoResponse, cancellationToken);
        });

    [Description("Generate Agent Skill packages with Loreto from a protected SharePoint/OneDrive or HTTP file URL by downloading the file first, uploading it to Loreto, uploading each generated .zip package with the default MCP SharePoint/OneDrive upload helper, and returning resource links plus structured Loreto metadata.")]
    [McpServerTool(Title = "Generate Loreto skill packages from file URL", Name = "loreto_skills_upload_generate", ReadOnly = false, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> LoretoSkills_UploadGenerate(
        [Description("PDF, HTML, JPEG, PNG, GIF, or WebP file URL. Secured SharePoint and OneDrive links are supported.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Test language. Allowed values: python, typescript, javascript. Default: python.")] string testLanguage = "python",
        [Description("Include Mermaid diagrams in generated SKILL.md files. Default: true.")] bool includeVisuals = true,
        [Description("Optional 1-3 sentence hint for generation, max 500 characters.")] string? context = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);
            ValidateTestLanguage(testLanguage);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var file = files?.FirstOrDefault() ?? throw new InvalidOperationException("No file found for Loreto upload generation input.");

            var client = serviceProvider.GetRequiredService<LoretoClient>();
            var loretoResponse = await client.UploadAndGenerateAsync(
                ResolveFileName(file, fileUrl),
                file.Contents,
                file.MimeType,
                testLanguage,
                includeVisuals,
                NormalizeContext(context),
                cancellationToken);

            return await BuildPackageUploadResponseAsync(serviceProvider, requestContext, loretoResponse, cancellationToken);
        });

    [Description("Check Loreto API health and return the JSON status as structured content.")]
    [McpServerTool(Title = "Loreto health check", Name = "loreto_health", ReadOnly = true, OpenWorld = true, Destructive = false)]
    public static async Task<CallToolResult?> Loreto_Health(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var client = serviceProvider.GetRequiredService<LoretoClient>();
            return await client.HealthAsync(cancellationToken);
        }));

    private static async Task<CallToolResult> BuildPackageUploadResponseAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        JsonElement loretoResponse,
        CancellationToken cancellationToken)
    {
        var packages = BuildSkillPackages(loretoResponse).ToList();
        if (packages.Count == 0)
            throw new InvalidOperationException("Loreto response did not contain any generated skills.");

        var links = new List<ResourceLinkBlock>();
        var packageNodes = new JsonArray();

        foreach (var package in packages)
        {
            var uploadName = $"{requestContext.ToOutputFileName()}_{SanitizeFileName(package.SkillName)}.zip";
            var uploaded = await requestContext.Server.Upload(
                serviceProvider,
                uploadName,
                BinaryData.FromBytes(package.ZipBytes),
                cancellationToken);

            if (uploaded is not null)
                links.Add(uploaded);

            packageNodes.Add(new JsonObject
            {
                ["skillName"] = package.SkillName,
                ["rank"] = package.Rank.HasValue ? JsonValue.Create(package.Rank.Value) : null,
                ["themeSummary"] = package.ThemeSummary,
                ["fileCount"] = package.FileCount,
                ["zipFileName"] = uploadName,
                ["zipSize"] = package.ZipBytes.Length,
                ["validSkillPackage"] = true,
                ["resource"] = uploaded is null ? null : new JsonObject
                {
                    ["uri"] = uploaded.Uri,
                    ["name"] = uploaded.Name,
                    ["mimeType"] = uploaded.MimeType ?? ZipMimeType,
                    ["description"] = uploaded.Description,
                    ["size"] = uploaded.Size.HasValue ? JsonValue.Create(uploaded.Size.Value) : null
                }
            });
        }

        var structured = BuildStructuredContent(loretoResponse, packageNodes);

        return new CallToolResult
        {
            Meta = await requestContext.GetToolMeta(),
            Content = [.. links],
            StructuredContent = structured.ToJsonElement()
        };
    }

    private static JsonObject BuildStructuredContent(JsonElement loretoResponse, JsonArray packageNodes)
    {
        var root = new JsonObject
        {
            ["packages"] = packageNodes
        };

        CopyProperty(loretoResponse, root, "theme_plan", "themePlan");
        CopyProperty(loretoResponse, root, "source_analysis", "sourceAnalysis");
        CopyProperty(loretoResponse, root, "usage", "usage");

        return root;
    }

    private static void CopyProperty(JsonElement source, JsonObject target, string propertyName, string targetName)
    {
        if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(propertyName, out var value))
            target[targetName] = JsonNode.Parse(value.GetRawText());
    }

    private static IEnumerable<SkillPackage> BuildSkillPackages(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var skill in skills.EnumerateArray())
        {
            var skillName = GetRequiredString(skill, "skill_name");
            ValidateSkillName(skillName);

            var rank = TryGetInt(skill, "rank");
            var themeSummary = TryGetString(skill, "theme_summary");
            var files = skill.GetProperty("files");

            if (files.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Loreto skill '{skillName}' does not contain a files object.");

            var fileEntries = files.EnumerateObject().ToList();
            var skillMd = fileEntries.FirstOrDefault(f => IsSkillMdPath(f.Name, skillName));
            if (string.IsNullOrWhiteSpace(skillMd.Name))
                throw new InvalidOperationException($"Loreto skill '{skillName}' is not a valid Agent Skill package because SKILL.md is missing.");

            ValidateSkillMd(skillName, skillMd.Value.GetString());

            yield return new SkillPackage(skillName, rank, themeSummary, CreateZip(skillName, fileEntries), fileEntries.Count);
        }
    }

    private static byte[] CreateZip(string skillName, IReadOnlyList<JsonProperty> fileEntries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in fileEntries)
            {
                if (file.Value.ValueKind != JsonValueKind.String)
                    continue;

                var entryName = BuildZipEntryName(skillName, file.Name);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(file.Value.GetString() ?? string.Empty);
                entryStream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    private static string BuildZipEntryName(string skillName, string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("../", StringComparison.Ordinal)
            || normalized.Contains("/..", StringComparison.Ordinal)
            || normalized.Equals("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Loreto returned unsafe skill package path '{path}'.");
        }

        if (normalized.StartsWith(skillName + "/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return $"{skillName}/{normalized}";
    }

    private static bool IsSkillMdPath(string path, string skillName)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        return normalized.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals($"{skillName}/SKILL.md", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFileName(FileItem file, string fileUrl)
    {
        if (!string.IsNullOrWhiteSpace(file.Filename))
            return file.Filename;

        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return "source";
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = TryGetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Loreto response is missing required property '{propertyName}'.");

        return value;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static int? TryGetInt(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : null;

    private static void ValidateEnums(string sourceType, string testLanguage)
    {
        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        if (normalizedSourceType is not ("auto" or "youtube" or "article" or "pdf" or "image"))
            throw new ArgumentOutOfRangeException(nameof(sourceType), "Allowed values: auto, youtube, article, pdf, image.");

        ValidateTestLanguage(testLanguage);
    }

    private static void ValidateTestLanguage(string testLanguage)
    {
        var normalizedTestLanguage = testLanguage.Trim().ToLowerInvariant();
        if (normalizedTestLanguage is not ("python" or "typescript" or "javascript"))
            throw new ArgumentOutOfRangeException(nameof(testLanguage), "Allowed values: python, typescript, javascript.");
    }

    private static string? NormalizeContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return null;

        var normalized = context.Trim();
        if (normalized.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(context), "Loreto context must be 500 characters or fewer.");

        return normalized;
    }

    private static IReadOnlyList<string> ParseDelimited(string? value, int max)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)];

    private static void ValidateSkillName(string skillName)
    {
        if (!SkillNamePattern().IsMatch(skillName))
            throw new InvalidOperationException($"Loreto returned invalid Agent Skill name '{skillName}'. Skill names must be lowercase alphanumeric with single hyphens.");
    }

    private static void ValidateSkillMd(string skillName, string? skillMd)
    {
        if (string.IsNullOrWhiteSpace(skillMd))
            throw new InvalidOperationException($"Loreto skill '{skillName}' contains an empty SKILL.md file.");

        if (!SkillMdNamePattern(skillName).IsMatch(skillMd))
            throw new InvalidOperationException($"Loreto skill '{skillName}' is not a valid Agent Skill package because SKILL.md frontmatter name does not match the package directory.");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '-');

        return value;
    }

    private static Regex SkillMdNamePattern(string skillName)
        => new($"^---\\s*$(?s:.*?)^name:\\s*[\"']?{Regex.Escape(skillName)}[\"']?\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [GeneratedRegex("^(?=.{1,64}$)[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SkillNamePattern();

    private sealed record SkillPackage(string SkillName, int? Rank, string? ThemeSummary, byte[] ZipBytes, int FileCount);
}
