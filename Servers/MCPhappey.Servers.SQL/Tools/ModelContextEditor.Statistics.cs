using System.ComponentModel;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextEditor
{
    [Description("Get aggregated statistics for all user-managed dynamic MCP servers.")]
    [McpServerTool(
        Title = "Get dynamic MCP server statistics",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_GetDynamicServerStatistics(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
            var servers = await serverRepository.GetServers(cancellationToken);

            var totalServers = servers.Count;
            var totalPrompts = servers.Sum(server => server.Prompts?.Count ?? 0);
            var totalResources = servers.Sum(server => server.Resources?.Count ?? 0);
            var totalResourceTemplates = servers.Sum(server => server.ResourceTemplates?.Count ?? 0);

            var allOwnerIds = servers
                .SelectMany(server => server.Owners)
                .Select(owner => owner.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalUniqueOwners = allOwnerIds.Count;
            var serversPerOwner = servers
                .SelectMany(server => server.Owners.Select(owner => new { server.Id, OwnerId = owner.Id }))
                .GroupBy(item => item.OwnerId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Count())
                .ToList();

            return new
            {
                totalServers,
                totalPrompts,
                totalResources,
                totalResourceTemplates,
                averagePromptsPerServer = Average(totalPrompts, totalServers),
                averageResourcesPerServer = Average(totalResources, totalServers),
                averageResourceTemplatesPerServer = Average(totalResourceTemplates, totalServers),
                totalUniqueOwners,
                averageServersPerOwner = Average(totalServers, totalUniqueOwners),
                minServersPerOwner = serversPerOwner.Count == 0 ? 0 : serversPerOwner.Min(),
                maxServersPerOwner = serversPerOwner.Count == 0 ? 0 : serversPerOwner.Max()
            };
        }));

    [Description("Get aggregated statistics for all static MCP servers.")]
    [McpServerTool(
        Title = "Get static MCP server statistics",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_GetStaticServerStatistics(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var servers = serviceProvider.GetRequiredService<IReadOnlyList<ServerConfig>>()
                .Where(server => server.SourceType == ServerSourceType.Static)
                .ToList();

            var totalServers = servers.Count;
            var totalPrompts = servers.Sum(server => server.PromptList?.Prompts.Count ?? 0);
            var totalResources = servers.Sum(server => server.ResourceList?.Resources.Count ?? 0);
            var totalResourceTemplates = servers.Sum(server => server.ResourceTemplateList?.ResourceTemplates.Count ?? 0);

            return await Task.FromResult(new
            {
                totalServers,
                totalPrompts,
                totalResources,
                totalResourceTemplates,
                averagePromptsPerServer = Average(totalPrompts, totalServers),
                averageResourcesPerServer = Average(totalResources, totalServers),
                averageResourceTemplatesPerServer = Average(totalResourceTemplates, totalServers)
            });
        }));

    [Description("List user-managed dynamic MCP servers that the current user can edit.")]
    [McpServerTool(
        Title = "List editable MCP servers",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_ListMcpServers(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("When true, include full server details such as prompts, resources, templates and tools.")]
        bool includeDetails = false,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var defaultIcons = serviceProvider.GetRequiredService<List<ServerIcon>>();
            var servers = await serviceProvider.GetServers(cancellationToken);

            return new
            {
                includeDetails,
                servers = servers
                    .OrderBy(server => server.Name)
                    .Select(server => ToServerResult(server, defaultIcons, includeDetails))
                    .ToList()
            };
        }));

    private static double Average(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

    private static object ToServerResult(SQL.Models.Server server, List<ServerIcon> defaultIcons, bool includeDetails)
    {
        var mcpServer = server.ToMcpServer(defaultIcons);

        return new
        {
            mcpServer.ServerInfo?.Name,
            mcpServer.ServerInfo?.Title,
            mcpServer.ServerInfo?.Description,
            mcpServer.ServerInfo?.WebsiteUrl,
            server.Secured,
            server.Hidden,
            Owners = server.Owners.Select(owner => owner.Id).Order().ToList(),
            SecurityGroups = server.Groups.Select(group => group.Id).Order().ToList(),
            Details = includeDetails
                ? new
                {
                    Prompts = server.Prompts?.OrderBy(prompt => prompt.Name).Select(prompt => prompt.ToPromptTemplate()).ToList(),
                    Resources = server.Resources?.OrderBy(resource => resource.Name).Select(resource => resource.ToResource()).ToList(),
                    ResourceTemplates = server.ResourceTemplates?.OrderBy(template => template.Name).Select(template => template.ToResourceTemplate()).ToList(),
                    Tools = server.Plugins?.OrderBy(plugin => plugin.PluginName).Select(plugin => plugin.PluginName).ToList()
                }
                : null
        };
    }
}
