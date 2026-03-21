using System.ComponentModel;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Azure;

public static class AzureSkillsService
{
    [Description("Search Azure-backed agent skills and return an OpenAI-compatible skill list shape.")]
    [McpServerTool(
        Title = "Search Azure Skills",
        Name = "azure_skills_search",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkills_Search(
        [Description("Optional search text matched against skill name and description.")] string? query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of skills to return. Default: 50.")] int limit = 50,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>();
                return storage is null
                    ? AzureSkillListResponse.Empty("SkillsStorage is not configured.")
                    : await storage.SearchSkillsAsync(query, limit, cancellationToken);
            }));

    [Description("Activate an Azure-backed agent skill by returning the full SKILL.md content for the requested skill and version.")]
    [McpServerTool(
        Title = "Activate Azure Skill",
        Name = "azure_skills_activate",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkills_Activate(
        [Description("The skill name.")] string skillName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional skill version. When omitted, the latest version is used.")] string? version = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                    ?? throw new InvalidOperationException("SkillsStorage is not configured.");

                return await storage.ActivateSkillAsync(skillName, version, cancellationToken);
            }));

    [Description("Read a specific file from an Azure-backed agent skill version by relative path.")]
    [McpServerTool(
        Title = "Read Azure Skill File",
        Name = "azure_skills_read_file",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> AzureSkills_ReadFile(
        [Description("The skill name.")] string skillName,
        [Description("Relative path inside the skill version, for example references/REFERENCE.md.")] string relativePath,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional skill version. When omitted, the latest version is used.")] string? version = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var storage = serviceProvider.GetService<AzureSkillsStorageService>()
                    ?? throw new InvalidOperationException("SkillsStorage is not configured.");

                return await storage.ReadSkillFileStrictAsync(skillName, version, relativePath, cancellationToken);
            }));
}
