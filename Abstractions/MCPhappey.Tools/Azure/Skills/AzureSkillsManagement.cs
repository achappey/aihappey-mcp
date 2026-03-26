using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure;

public static class AzureSkillsManagement
{
    [Description("Please confirm to delete: {0}")]
    public sealed class AzureSkillDeleteConfirmation : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = string.Empty;
    }

    [Description("Please provide exactly one upload source for the Azure skill.")]
    public sealed class AzureSkillUploadSource
    {
        [JsonPropertyName("fileUrl")]
        [Description("Direct file URL to a zip file containing the skill. Supports SharePoint, OneDrive, and HTTPS.")]
        public string? FileUrl { get; set; }

        [JsonPropertyName("folderUrl")]
        [Description("SharePoint or OneDrive folder URL containing the skill files. Leave empty when fileUrl is provided.")]
        public string? FolderUrl { get; set; }
    }

    [Description("Please provide the default version to use for the skill.")]
    public sealed class AzureSkillDefaultVersionInput
    {
        [JsonPropertyName("defaultVersion")]
        [Required]
        [Description("The version to set as default, for example 1 or 2.")]
        public string DefaultVersion { get; set; } = string.Empty;
    }

    [Description("Create a new Azure-backed agent skill from a zip fileUrl or SharePoint/OneDrive folderUrl.")]
    [McpServerTool(
        Title = "Create Azure Skill",
        Name = "azure_skills_management_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkillsManagement_Create(
        [Description("Zip file URL containing the skill files. Leave empty when folderUrl is provided.")] string? fileUrl,
        [Description("SharePoint or OneDrive folder URL containing the skill files. Leave empty when fileUrl is provided.")] string? folderUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<AzureSkillMutationResult>(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                    ?? throw new InvalidOperationException("SkillsStorage is not configured.");

                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new AzureSkillUploadSource
                    {
                        FileUrl = fileUrl,
                        FolderUrl = folderUrl
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                ValidateExclusiveSource(typed.FileUrl, typed.FolderUrl);

                var files = await LoadUploadFilesAsync(serviceProvider, requestContext, typed.FileUrl, typed.FolderUrl, cancellationToken);
                return await storage.CreateSkillAsync(files, cancellationToken);
            }));

    [Description("Create a new immutable Azure-backed agent skill version from a zip fileUrl or SharePoint/OneDrive folderUrl.")]
    [McpServerTool(
        Title = "Create Azure Skill Version",
        Name = "azure_skills_management_create_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkillsManagement_CreateVersion(
        [Description("The skill name.")] string skillName,
        [Description("Zip file URL containing the skill files. Leave empty when folderUrl is provided.")] string? fileUrl,
        [Description("SharePoint or OneDrive folder URL containing the skill files. Leave empty when fileUrl is provided.")] string? folderUrl,
        [Description("Whether the new version should immediately become the default version.")] bool makeDefault,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<AzureSkillMutationResult>(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                    ?? throw new InvalidOperationException("SkillsStorage is not configured.");

                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new AzureSkillUploadSource
                    {
                        FileUrl = fileUrl,
                        FolderUrl = folderUrl
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                ValidateExclusiveSource(typed.FileUrl, typed.FolderUrl);
                var files = await LoadUploadFilesAsync(serviceProvider, requestContext, typed.FileUrl, typed.FolderUrl, cancellationToken);

                return await storage.CreateVersionAsync(skillName, files, makeDefault, cancellationToken);
            }));

    [Description("Update the default version pointer for an Azure-backed agent skill.")]
    [McpServerTool(
        Title = "Set Azure Skill Default Version",
        Name = "azure_skills_management_set_default_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkillsManagement_SetDefaultVersion(
        [Description("The skill name.")] string skillName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The version to set as default.")] string? defaultVersion = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<AzureSkillMutationResult>(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                    ?? throw new InvalidOperationException("SkillsStorage is not configured.");

                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new AzureSkillDefaultVersionInput
                    {
                        DefaultVersion = defaultVersion ?? string.Empty
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                return await storage.SetDefaultVersionAsync(skillName, typed.DefaultVersion, cancellationToken);
            }));

    [Description("Delete an Azure-backed agent skill by skill name.")]
    [McpServerTool(
        Title = "Delete Azure Skill",
        Name = "azure_skills_management_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AzureSkillsManagement_Delete(
        [Description("The skill name.")] string skillName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                ?? throw new InvalidOperationException("SkillsStorage is not configured.");

            return await requestContext.ConfirmAndDeleteAsync<AzureSkillDeleteConfirmation>(
                expectedName: skillName,
                deleteAction: async _ => await storage.DeleteSkillAsync(skillName, cancellationToken),
                successText: $"Azure skill '{skillName}' deleted successfully!",
                ct: cancellationToken);
        });

    [Description("Delete an Azure-backed agent skill version by skill name and version.")]
    [McpServerTool(
        Title = "Delete Azure Skill Version",
        Name = "azure_skills_management_delete_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AzureSkillsManagement_DeleteVersion(
        [Description("The skill name.")] string skillName,
        [Description("The version to delete.")] string version,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                ?? throw new InvalidOperationException("SkillsStorage is not configured.");

            return await requestContext.ConfirmAndDeleteAsync<AzureSkillDeleteConfirmation>(
                expectedName: version,
                deleteAction: async _ => await storage.DeleteVersionAsync(skillName, version, cancellationToken),
                successText: $"Azure skill version '{version}' for skill '{skillName}' deleted successfully!",
                ct: cancellationToken);
        });

    private static void ValidateExclusiveSource(string? fileUrl, string? folderUrl)
    {
        var hasFileUrl = !string.IsNullOrWhiteSpace(fileUrl);
        var hasFolderUrl = !string.IsNullOrWhiteSpace(folderUrl);

        if (hasFileUrl == hasFolderUrl)
            throw new ValidationException("Provide exactly one upload source: either fileUrl or folderUrl.");
    }

    private static async Task<IReadOnlyList<AzureSkillUploadFile>> LoadUploadFilesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fileUrl,
        string? folderUrl,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var downloads = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
            var zipFile = downloads.FirstOrDefault()
                ?? throw new InvalidOperationException("Zip file could not be downloaded from fileUrl.");

            return AzureSkillsStorageService.ExtractFilesFromZip(zipFile);
        }

        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        var files = await BuildSkillFilesFromSharePointFolderAsync(graphClient, folderUrl!, cancellationToken);
        return AzureSkillsStorageService.NormalizeUploadedFiles(files);
    }

    private static async Task<List<(string filename, BinaryData contents, string mimeType)>> BuildSkillFilesFromSharePointFolderAsync(
        GraphServiceClient graph,
        string folderUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderUrl))
            throw new ArgumentNullException(nameof(folderUrl));

        var token = ToSharingToken(folderUrl);

        var rootItem = await graph.Shares[token].DriveItem.GetAsync(cancellationToken: ct)
                       ?? throw new InvalidOperationException("Folder not found via Shares API.");

        if (rootItem.Folder is null)
            throw new InvalidOperationException("URL must point to a folder.");

        var driveId = rootItem.ParentReference?.DriveId
                      ?? throw new InvalidOperationException("DriveId missing on root item.");

        var rootPath = rootItem.Name ?? "skill";
        var files = new List<(string filename, BinaryData contents, string mimeType)>();
        await CollectFilesRecursiveAsync(graph, driveId, rootItem.Id!, rootPath, files, ct);
        return files;
    }

    private static async Task CollectFilesRecursiveAsync(
        GraphServiceClient graph,
        string driveId,
        string itemId,
        string pathPrefix,
        List<(string filename, BinaryData contents, string mimeType)> sink,
        CancellationToken ct)
    {
        var page = await graph.Drives[driveId].Items[itemId].Children.GetAsync(rc =>
        {
            rc.QueryParameters.Top = 200;
        }, ct);

        while (true)
        {
            foreach (var child in page?.Value ?? Enumerable.Empty<DriveItem>())
            {
                ct.ThrowIfCancellationRequested();

                if (child.Folder != null)
                {
                    var nextPrefix = $"{pathPrefix}/{child.Name}";
                    await CollectFilesRecursiveAsync(graph, driveId, child.Id!, nextPrefix, sink, ct);
                }
                else if (child.File != null)
                {
                    using var download = await graph.Drives[driveId].Items[child.Id!].Content.GetAsync(cancellationToken: ct);
                    if (download is null)
                        continue;

                    using var memory = new MemoryStream();
                    await download.CopyToAsync(memory, ct);

                    sink.Add(($"{pathPrefix}/{child.Name}", BinaryData.FromBytes(memory.ToArray()), child.File.MimeType ?? "application/octet-stream"));
                }
            }

            var nextLink = page?.OdataNextLink;
            if (string.IsNullOrWhiteSpace(nextLink))
                break;

            page = await graph.Drives[driveId].Items[itemId].Children.WithUrl(nextLink!).GetAsync(cancellationToken: ct);
        }
    }

    private static string ToSharingToken(string url)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return "u!" + b64;
    }
}
