using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SandBase.Skills;

public static class SandBaseSkills
{
    [Description("Please confirm to delete Skill: {0}")]
    public sealed class ConfirmSkill : IHasName
    {
        [Description("Type the Skill UUID to confirm soft deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Upload a ZIP from an HTTPS SharePoint/OneDrive URL to SandBase Skill storage. Returns the private skill_file_url; register it separately using create. Never place API keys in ZIP bundles.")]
    [McpServerTool(Title = "Upload SandBase Skill ZIP", Name = "sandbase_skills_upload", ReadOnly = false, Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Upload(string fileUrl, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.UserInfo.Length != 0 || !(uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("1drv.ms", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("onedrive.live.com", StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException("fileUrl must be an HTTPS SharePoint/OneDrive link.");
            var file = (await services.GetRequiredService<DownloadService>()
                .DownloadContentAsync(services, context.Server, fileUrl, cancellationToken)).FirstOrDefault()
                ?? throw new ValidationException("Skill ZIP was not found.");
            var bytes = file.Contents.ToArray();
            if (bytes.Length is 0 or > 50 * 1024 * 1024) throw new ValidationException("Skill ZIP must be between 1 byte and 50 MB.");
            ValidateZip(bytes);
            var filename = Path.GetFileName(file.Filename);
            if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) filename = "skill.zip";
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(part, "file", filename);
            return await SandBaseClient.SendAsync(services, HttpMethod.Post, "v1/skills/files",
                cancellationToken: cancellationToken, content: form);
        }));

    [Description("Register a SandBase Skill from a previously uploaded private SandBase bundle URL or a Git URL. For Agent runtime, use an uploaded ZIP. The response name is the immutable vendor_slug/plugin_slug reference for agents.")]
    [McpServerTool(Title = "Create SandBase Skill", Name = "sandbase_skills_create", ReadOnly = false, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Create(string name, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string? description = null, string? categoriesJson = null,
        string? skillFileUrl = null, string? gitUrl = null, string? previewImageUrlsJson = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(skillFileUrl) == string.IsNullOrWhiteSpace(gitUrl))
                throw new ValidationException("Supply exactly one source: skillFileUrl or gitUrl.");
            var body = new JsonObject { ["name"] = DisplayName(name) };
            if (description is not null) body["description"] = Description(description);
            if (categoriesJson is not null) body["categories"] = SandBaseClient.Array(categoriesJson, "categoriesJson");
            if (skillFileUrl is not null) body["skill_file_url"] = SandBaseClient.SafeUrl(skillFileUrl, "media.sandbase.ai");
            if (gitUrl is not null) body["git_url"] = Https(gitUrl);
            if (previewImageUrlsJson is not null) body["preview_image_urls"] = SandBaseClient.Array(previewImageUrlsJson, "previewImageUrlsJson");
            return await SandBaseClient.SendAsync(services, HttpMethod.Post, "v1/skills", body, cancellationToken);
        }));

    [Description("Update SandBase Skill display fields and optionally its uploaded bundle or previews. Reads the current Skill first: omitted name/description/categories are preserved (SandBase PUT would otherwise clear them). The full Skill reference does not change.")]
    [McpServerTool(Title = "Update SandBase Skill", Name = "sandbase_skills_update", ReadOnly = false, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Update(string skillId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string? name = null, string? description = null,
        string? categoriesJson = null, string? skillFileUrl = null, string? previewImageUrlsJson = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            if (name is null && description is null && categoriesJson is null && skillFileUrl is null && previewImageUrlsJson is null)
                throw new ValidationException("Provide at least one change.");
            var path = $"v1/skills/{SandBaseClient.SkillId(skillId)}";
            var current = await SandBaseClient.SendAsync(services, HttpMethod.Get, path, cancellationToken: cancellationToken) as JsonObject
                ?? throw new ValidationException("Skill detail must be an object.");
            // PUT clears omitted display fields; use current values to preserve them.
            var body = new JsonObject
            {
                ["name"] = name is null ? current["display_name"]?.GetValue<string>() ?? "" : DisplayName(name),
                ["description"] = description is null ? current["description"]?.GetValue<string>() ?? "" : Description(description),
                ["categories"] = categoriesJson is null ? current["categories"]?.DeepClone() ?? new JsonArray() : SandBaseClient.Array(categoriesJson, "categoriesJson")
            };
            if (skillFileUrl is not null) body["skill_file_url"] = SandBaseClient.SafeUrl(skillFileUrl, "media.sandbase.ai");
            if (previewImageUrlsJson is not null) body["preview_image_urls"] = SandBaseClient.Array(previewImageUrlsJson, "previewImageUrlsJson");
            return await SandBaseClient.SendAsync(services, HttpMethod.Put, path, body, cancellationToken);
        }));

    [Description("Soft-delete an organization-owned SandBase Skill after explicit typed confirmation.")]
    [McpServerTool(Title = "Delete SandBase Skill", Name = "sandbase_skills_delete", ReadOnly = false, Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Delete(string skillId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var id = SandBaseClient.SkillId(skillId);
            return await context.ConfirmAndDeleteAsync<ConfirmSkill>(id,
                async ct => _ = await SandBaseClient.SendAsync(services, HttpMethod.Delete, $"v1/skills/{id}", cancellationToken: ct),
                $"SandBase Skill '{id}' deleted.", cancellationToken);
        });

    private static string DisplayName(string value)
    {
        var result = SandBaseClient.Required(value, "name");
        return result.Length <= 100 ? result : throw new ValidationException("name exceeds 100 characters.");
    }

    private static string Description(string value)
        => value.Length <= 1000 ? value : throw new ValidationException("description exceeds 1000 characters.");

    private static string Https(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            ? url : throw new ValidationException("gitUrl must be an HTTPS URL without embedded credentials.");

    private static void ValidateZip(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > 1024) throw new ValidationException("Skill ZIP contains more than 1024 entries.");
            if (zip.Entries.Sum(e => e.Length) > 60L * 1024 * 1024)
                throw new ValidationException("Skill ZIP exceeds 60 MB uncompressed.");
            if (zip.Entries.Count(e => e.Name.Equals("SKILL.md", StringComparison.Ordinal)) != 1)
                throw new ValidationException("Skill ZIP must contain exactly one SKILL.md.");
            if (zip.Entries.Any(e => e.FullName.StartsWith('/') || e.FullName.Contains('\\')
                || e.FullName.Split('/').Any(part => part == "..")))
                throw new ValidationException("Skill ZIP contains unsafe paths.");
        }
        catch (InvalidDataException) { throw new ValidationException("File is not a readable ZIP archive."); }
    }
}
