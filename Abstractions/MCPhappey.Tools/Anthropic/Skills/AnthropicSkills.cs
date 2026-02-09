using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Anthropic.SDK;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;
using Microsoft.Graph.Beta.Models;
using Microsoft.Graph.Beta;
using System.Text;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.Anthropic.Skills;

public static class AnthropicSkills
{
    private static readonly string[] BetaFeatures =
       [
            "skills-2025-10-02"
       ];

    [Description("Please confirm to delete: {0}")]
    public class AnthropicDeleteSkill : IHasName
    {
        [JsonPropertyName("name")]
        [Description("Confirm deletion.")]
        public string Name { get; set; } = default!;
    }

    [Description("Delete an Anthropic Skill version by Skill ID and Version ID.")]
    [McpServerTool(
        Title = "Delete Anthropic Skill Version",
        Name = "anthropic_delete_skill_version",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicSkills_DeleteVersion(
        [Description("ID of the skill.")] string skillId,
        [Description("ID of the skill version.")] string versionId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
            ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

            var antSettings = serviceProvider.GetRequiredService<AnthropicSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", antSettings.ApiKey);
            httpClient.DefaultRequestHeaders.Add("anthropic-beta", string.Join(',', BetaFeatures));
            httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            return await requestContext.ConfirmAndDeleteAsync<AnthropicDeleteSkill>(
                versionId,
                async _ =>
                {
                    using var req = new HttpRequestMessage(
                        HttpMethod.Delete,
                        $"https://api.anthropic.com/v1/skills/{skillId}/versions/{versionId}"
                    );

                    using var resp = await httpClient.SendAsync(req, cancellationToken);
                    var content = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"{resp.StatusCode}: {content}");
                },
                successText: $"Skill version '{versionId}' of skill '{skillId}' deleted successfully!",
                ct: cancellationToken);
        }));

    [Description("Delete an Anthropic Skill by ID.")]
    [McpServerTool(
        Title = "Delete Anthropic Skill",
        Name = "anthropic_delete_skill",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> AnthropicSkills_Delete(
        [Description("ID of the skill to delete.")] string skillId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var antSettings = serviceProvider.GetRequiredService<AnthropicSettings>();
            var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("x-api-key", antSettings.ApiKey);
            httpClient.DefaultRequestHeaders.Add("anthropic-beta", string.Join(',', BetaFeatures));
            httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            return await requestContext.ConfirmAndDeleteAsync<AnthropicDeleteSkill>(
                expectedName: skillId,
                deleteAction: async _ =>
                {
                    using var req = new HttpRequestMessage(
                        HttpMethod.Delete,
                        $"https://api.anthropic.com/v1/skills/{skillId}"
                    );
                    using var resp = await httpClient.SendAsync(req, cancellationToken);
                    var responseText = await resp.Content.ReadAsStringAsync(cancellationToken);

                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"{resp.StatusCode}: {responseText}");
                },
                successText: $"Skill '{skillId}' deleted successfully!",
                ct: cancellationToken);
        }));


    [Description("Create a new Anthropic Skill from a SharePoint folder.")]
    [McpServerTool(Title = "Create Anthropic Skill",
        OpenWorld = false,
        ReadOnly = false)]
    public static async Task<CallToolResult?> AnthropicSkills_Create(
          [Description("Skill display title")]
            string displayTitle,
          [Description("SharePoint folder url with the skill source files")]
            string folderUrl,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithOboGraphClient(async (client) =>
          await requestContext.WithStructuredContent(async () =>
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var antSettings = serviceProvider.GetRequiredService<AnthropicSettings>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", antSettings.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-beta", string.Join(',', BetaFeatures));
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var antClient = new AnthropicClient(antSettings.ApiKey, httpClient);

        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AnthropicNewSkill
        {
            DisplayTitle = displayTitle,
        }, cancellationToken);

        var files = await client.BuildSkillFilesFromSharePointFolderAsync(
            folderUrl,
            cancellationToken);

        return await antClient.Skills.CreateSkillFromStreamsAsync(typed.DisplayTitle, files, cancellationToken);
    })));

    [Description("Create a new Anthropic Skill version from a SharePoint folder.")]
    [McpServerTool(Title = "Create Anthropic Skill vresion",
        OpenWorld = false,
        ReadOnly = false)]
    public static async Task<CallToolResult?> AnthropicSkills_CreateVersion(
          [Description("Id of the skill to create the new version for")]
            string skillId,
          [Description("SharePoint folder url with the new version skill source files")]
            string folderUrl,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithOboGraphClient(async (client) =>
          await requestContext.WithStructuredContent(async () =>
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var antSettings = serviceProvider.GetRequiredService<AnthropicSettings>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", antSettings.ApiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-beta", string.Join(',', BetaFeatures));
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var antClient = new AnthropicClient(antSettings.ApiKey, httpClient);

        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AnthropicNewSkillVersion
        {
            SkillId = skillId,
        }, cancellationToken);

        var files = await client.BuildSkillFilesFromSharePointFolderAsync(
            folderUrl,
            cancellationToken);

        return await antClient.Skills.CreateSkillVersionFromStreamsAsync(typed.SkillId, files, cancellationToken);
    })));

    [Description("Please fill in the skill details.")]
    public class AnthropicNewSkill
    {
        [Required]
        [Description("Skill display title")]
        [JsonPropertyName("displayTitle")]
        public string DisplayTitle { get; set; } = null!;
    }

    [Description("Please fill in the skill id to confirm.")]
    public class AnthropicNewSkillVersion
    {
        [Required]
        [Description("Skill id")]
        [JsonPropertyName("skillId")]
        public string SkillId { get; set; } = null!;
    }

    private static async Task<List<(string filename, Stream stream, string mimeType)>> BuildSkillFilesFromSharePointFolderAsync(
          this GraphServiceClient graph,
          string folderUrl,
          CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderUrl))
            throw new ArgumentNullException(nameof(folderUrl));

        var token = ToSharingToken(folderUrl);

        // Resolve the shared item (must be a folder)
        var rootItem = await graph.Shares[token].DriveItem.GetAsync(cancellationToken: ct)
                        ?? throw new InvalidOperationException("Folder not found via Shares API.");
        if (rootItem.Folder is null)
            throw new InvalidOperationException("URL must point to a folder.");

        var driveId = rootItem.ParentReference?.DriveId
                      ?? throw new InvalidOperationException("DriveId missing on root item.");
        var rootPath = rootItem.Name ?? "skill";

        var files = new List<(string filename, Stream stream, string mimeType)>();
        await CollectFilesRecursiveAsync(graph, driveId, rootItem.Id!, rootPath, files, ct);

        // Anthropic requires a SKILL.md somewhere in the tree
        if (!files.Any(f => f.filename.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The folder must contain a SKILL.md file.");

        return files;
    }

    private static async Task CollectFilesRecursiveAsync(
        GraphServiceClient graph,
        string driveId,
        string itemId,
        string pathPrefix,
        List<(string filename, Stream stream, string mimeType)> sink,
        CancellationToken ct)
    {
        // First page
        var page = await graph.Drives[driveId].Items[itemId].Children.GetAsync(rc =>
        {
            rc.QueryParameters.Top = 200; // optional, fewer roundtrips
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
                    // Download file stream
                    using var download = await graph.Drives[driveId].Items[child.Id!].Content.GetAsync(cancellationToken: ct);
                    if (download is null) continue;

                    var ms = new MemoryStream();
                    await download.CopyToAsync(ms, ct);
                    ms.Position = 0;

                    string relName = $"{pathPrefix}/{child.Name}";
                    string mime = child.File.MimeType ?? "application/octet-stream";

                    // Keep order: (filename, stream, mimeType)
                    sink.Add((relName, ms, mime));
                }
            }

            // Follow @odata.nextLink using WithUrl
            var nextLink = page?.OdataNextLink;
            if (string.IsNullOrEmpty(nextLink)) break;

            page = await graph.Drives[driveId].Items[itemId].Children
                         .WithUrl(nextLink!)
                         .GetAsync(cancellationToken: ct);
        }
    }

    private static string ToSharingToken(string url)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "u!" + b64;
    }
}


public class AnthropicSettings
{
    public string ApiKey { get; set; } = default!;
}
