using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Tools.Extensions;

namespace MCPhappey.Tools.OpenAI.Skills;

public static class OpenAISkills
{
    private const string SkillsEndpoint = "https://api.openai.com/v1/skills";

    [Description("Please confirm to delete: {0}")]
    public class OpenAIDeleteSkill : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = default!;
    }

    [Description("Create a new OpenAI skill from a SharePoint folder or direct zip file URL.")]
    [McpServerTool(
        Title = "Create OpenAI Skill",
        Name = "openai_skills_create",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OpenAISkills_Create(
        [Description("SharePoint folder URL containing the skill source files. Leave empty when zipFileUrl is provided.")]
        string? folderUrl,
        [Description("Direct URL to a zip file containing the skill source files. Leave empty when folderUrl is provided.")]
        string? zipFileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new OpenAINewSkill
                {
                    FolderUrl = folderUrl,
                    ZipFileUrl = zipFileUrl
                },
                cancellationToken);

            if (notAccepted is not null)
                return notAccepted;

            ArgumentNullException.ThrowIfNull(typed);
            ValidateExclusiveUploadSource(typed.FolderUrl, typed.ZipFileUrl);

            if (HasFolderSource(typed.FolderUrl))
            {
                return await requestContext.WithOboGraphClient(async graphClient =>
                    await requestContext.WithStructuredContent(async () =>
                    {
                        using var form = await BuildMultipartFromSharePointFolderAsync(graphClient, typed.FolderUrl!, cancellationToken);
                        return await SendMultipartAsync(serviceProvider, HttpMethod.Post, SkillsEndpoint, form, cancellationToken);
                    }));
            }

            return await requestContext.WithStructuredContent(async () =>
            {
                using var form = await BuildMultipartFromZipUrlAsync(serviceProvider, requestContext.Server, typed.ZipFileUrl!, cancellationToken);
                return await SendMultipartAsync(serviceProvider, HttpMethod.Post, SkillsEndpoint, form, cancellationToken);
            });
        });

    [Description("Update the default version pointer for an OpenAI skill.")]
    [McpServerTool(
        Title = "Update OpenAI Skill Default Version",
        Name = "openai_skills_update_default_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OpenAISkills_UpdateDefaultVersion(
        [Description("ID of the skill to update.")] string skillId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The version number to set as default.")] string? defaultVersion = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new OpenAIUpdateSkillDefaultVersion
                {
                    DefaultVersion = defaultVersion ?? string.Empty
                },
                cancellationToken);

            if (notAccepted is not null)
                return notAccepted;

            ArgumentNullException.ThrowIfNull(typed);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.DefaultVersion);

            return await requestContext.WithStructuredContent(async () =>
            {
                var payload = JsonContent.Create(new { default_version = typed.DefaultVersion });
                return await SendJsonAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{SkillsEndpoint}/{Uri.EscapeDataString(skillId)}",
                    payload,
                    cancellationToken);
            });
        });

    [Description("Delete an OpenAI skill by ID.")]
    [McpServerTool(
        Title = "Delete OpenAI Skill",
        Name = "openai_skills_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OpenAISkills_Delete(
        [Description("ID of the skill to delete.")] string skillId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

            return await requestContext.ConfirmAndDeleteAsync<OpenAIDeleteSkill>(
                expectedName: skillId,
                deleteAction: async _ =>
                {
                    await SendDeleteAsync(
                        serviceProvider,
                        $"{SkillsEndpoint}/{Uri.EscapeDataString(skillId)}",
                        cancellationToken);
                },
                successText: $"OpenAI skill '{skillId}' deleted successfully!",
                ct: cancellationToken);
        });

    [Description("Create a new immutable version for an OpenAI skill from a SharePoint folder or direct zip file URL.")]
    [McpServerTool(
        Title = "Create OpenAI Skill Version",
        Name = "openai_skills_create_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> OpenAISkills_CreateVersion(
        [Description("ID of the skill to create a version for.")] string skillId,
        [Description("SharePoint folder URL containing the version source files. Leave empty when zipFileUrl is provided.")]
        string? folderUrl,
        [Description("Direct URL to a zip file containing the version source files. Leave empty when folderUrl is provided.")]
        string? zipFileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Whether the new version should immediately become the default version.")]
        bool? makeDefault = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new OpenAINewSkillVersion
                {
                    SkillId = skillId,
                    FolderUrl = folderUrl,
                    ZipFileUrl = zipFileUrl,
                    Default = makeDefault
                },
                cancellationToken);

            if (notAccepted is not null)
                return notAccepted;

            ArgumentNullException.ThrowIfNull(typed);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed.SkillId);
            ValidateExclusiveUploadSource(typed.FolderUrl, typed.ZipFileUrl);

            if (HasFolderSource(typed.FolderUrl))
            {
                return await requestContext.WithOboGraphClient(async graphClient =>
                    await requestContext.WithStructuredContent(async () =>
                    {
                        using var form = await BuildMultipartFromSharePointFolderAsync(graphClient, typed.FolderUrl!, cancellationToken, typed.Default);
                        return await SendMultipartAsync(
                            serviceProvider,
                            HttpMethod.Post,
                            $"{SkillsEndpoint}/{Uri.EscapeDataString(typed.SkillId)}/versions",
                            form,
                            cancellationToken);
                    }));
            }

            return await requestContext.WithStructuredContent(async () =>
            {
                using var form = await BuildMultipartFromZipUrlAsync(serviceProvider, requestContext.Server, typed.ZipFileUrl!, cancellationToken, typed.Default);
                return await SendMultipartAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{SkillsEndpoint}/{Uri.EscapeDataString(typed.SkillId)}/versions",
                    form,
                    cancellationToken);
            });
        });

    [Description("Delete an OpenAI skill version by skill ID and version number.")]
    [McpServerTool(
        Title = "Delete OpenAI Skill Version",
        Name = "openai_skills_delete_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> OpenAISkills_DeleteVersion(
        [Description("ID of the skill.")] string skillId,
        [Description("Version number to delete.")] string version,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
            ArgumentException.ThrowIfNullOrWhiteSpace(version);

            return await requestContext.ConfirmAndDeleteAsync<OpenAIDeleteSkill>(
                expectedName: version,
                deleteAction: async _ =>
                {
                    await SendDeleteAsync(
                        serviceProvider,
                        $"{SkillsEndpoint}/{Uri.EscapeDataString(skillId)}/versions/{Uri.EscapeDataString(version)}",
                        cancellationToken);
                },
                successText: $"OpenAI skill version '{version}' for skill '{skillId}' deleted successfully!",
                ct: cancellationToken);
        });

    [Description("Please fill in the OpenAI skill upload source. Provide exactly one source.")]
    public class OpenAINewSkill
    {
        [JsonPropertyName("folderUrl")]
        [Description("SharePoint folder URL containing the skill files. Mutually exclusive with zipFileUrl.")]
        public string? FolderUrl { get; set; }

        [JsonPropertyName("zipFileUrl")]
        [Description("Direct URL to a zip file containing the skill files. Mutually exclusive with folderUrl.")]
        public string? ZipFileUrl { get; set; }
    }

    [Description("Please fill in the OpenAI skill version details. Provide exactly one upload source.")]
    public class OpenAINewSkillVersion
    {
        [JsonPropertyName("skillId")]
        [Required]
        [Description("ID of the skill to create a version for.")]
        public string SkillId { get; set; } = string.Empty;

        [JsonPropertyName("folderUrl")]
        [Description("SharePoint folder URL containing the skill files. Mutually exclusive with zipFileUrl.")]
        public string? FolderUrl { get; set; }

        [JsonPropertyName("zipFileUrl")]
        [Description("Direct URL to a zip file containing the skill files. Mutually exclusive with folderUrl.")]
        public string? ZipFileUrl { get; set; }

        [JsonPropertyName("default")]
        [Description("Whether the created version should become the default version.")]
        public bool? Default { get; set; }
    }

    [Description("Please fill in the default version value.")]
    public class OpenAIUpdateSkillDefaultVersion
    {
        [JsonPropertyName("defaultVersion")]
        [Required]
        [Description("The version number to set as default.")]
        public string DefaultVersion { get; set; } = string.Empty;
    }

    private static async Task<JsonNode> SendMultipartAsync(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string requestUri,
        MultipartFormDataContent multipartContent,
        CancellationToken cancellationToken)
    {
        var httpClient = CreateHttpClient(serviceProvider);
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = multipartContent
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    private static async Task<JsonNode> SendJsonAsync(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string requestUri,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var httpClient = CreateHttpClient(serviceProvider);
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonResponseAsync(response, cancellationToken);
    }

    private static async Task SendDeleteAsync(
        IServiceProvider serviceProvider,
        string requestUri,
        CancellationToken cancellationToken)
    {
        var httpClient = CreateHttpClient(serviceProvider);
        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {responseText}");
    }

    private static HttpClient CreateHttpClient(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<OpenAISettings>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        return httpClient;
    }

    private static async Task<JsonNode> ReadJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {responseText}");

        if (string.IsNullOrWhiteSpace(responseText))
            return new JsonObject();

        return JsonNode.Parse(responseText) ?? new JsonObject();
    }

    private static async Task<MultipartFormDataContent> BuildMultipartFromZipUrlAsync(
        IServiceProvider serviceProvider,
        ModelContextProtocol.Server.McpServer mcpServer,
        string zipFileUrl,
        CancellationToken cancellationToken,
        bool? makeDefault = null)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var downloadedFiles = await downloadService.DownloadContentAsync(serviceProvider, mcpServer, zipFileUrl, cancellationToken);
        var zipFile = downloadedFiles.FirstOrDefault()
            ?? throw new InvalidOperationException("Zip file could not be downloaded.");

        var form = new MultipartFormDataContent();
        AddFile(form, "files", EnsureZipFileName(zipFile), zipFile.Contents, string.IsNullOrWhiteSpace(zipFile.MimeType) ? "application/zip" : zipFile.MimeType);

        if (makeDefault.HasValue)
            form.Add(new StringContent(makeDefault.Value ? "true" : "false"), "default");

        return form;
    }

    private static async Task<MultipartFormDataContent> BuildMultipartFromSharePointFolderAsync(
        GraphServiceClient graphClient,
        string folderUrl,
        CancellationToken cancellationToken,
        bool? makeDefault = null)
    {
        var files = await graphClient.BuildSkillFilesFromSharePointFolderAsync(folderUrl, cancellationToken);
        var form = new MultipartFormDataContent();

        foreach (var file in files)
        {
            AddFile(form, "files", file.filename, file.contents, file.mimeType);
        }

        if (makeDefault.HasValue)
            form.Add(new StringContent(makeDefault.Value ? "true" : "false"), "default");

        return form;
    }

    private static void AddFile(MultipartFormDataContent form, string fieldName, string fileName, BinaryData contents, string mimeType)
    {
        var fileContent = new ByteArrayContent(contents.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType)
            ? "application/octet-stream"
            : mimeType);

        form.Add(fileContent, fieldName, fileName);
    }

    private static string EnsureZipFileName(FileItem file)
    {
        var candidate = file.Filename;

        if (string.IsNullOrWhiteSpace(candidate)
            && Uri.TryCreate(file.Uri, UriKind.Absolute, out var uri))
        {
            candidate = Path.GetFileName(uri.LocalPath);
        }

        candidate = string.IsNullOrWhiteSpace(candidate) ? "skill.zip" : candidate;
        return candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? candidate : $"{candidate}.zip";
    }

    private static bool HasFolderSource(string? folderUrl)
        => !string.IsNullOrWhiteSpace(folderUrl);

    private static void ValidateExclusiveUploadSource(string? folderUrl, string? zipFileUrl)
    {
        var hasFolderUrl = !string.IsNullOrWhiteSpace(folderUrl);
        var hasZipFileUrl = !string.IsNullOrWhiteSpace(zipFileUrl);

        if (hasFolderUrl == hasZipFileUrl)
            throw new ValidationException("Provide exactly one upload source: either folderUrl or zipFileUrl.");
    }

    private static async Task<List<(string filename, BinaryData contents, string mimeType)>> BuildSkillFilesFromSharePointFolderAsync(
          this GraphServiceClient graph,
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

        if (!files.Any(f => f.filename.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The folder must contain a SKILL.md file.");

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

                    using var ms = new MemoryStream();
                    await download.CopyToAsync(ms, ct);

                    var relName = $"{pathPrefix}/{child.Name}";
                    var mime = child.File.MimeType ?? "application/octet-stream";

                    sink.Add((relName, BinaryData.FromBytes(ms.ToArray()), mime));
                }
            }

            var nextLink = page?.OdataNextLink;
            if (string.IsNullOrEmpty(nextLink))
                break;

            page = await graph.Drives[driveId].Items[itemId].Children
                .WithUrl(nextLink!)
                .GetAsync(cancellationToken: ct);
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

public class OpenAISettings
{
    public string ApiKey { get; set; } = default!;
}
