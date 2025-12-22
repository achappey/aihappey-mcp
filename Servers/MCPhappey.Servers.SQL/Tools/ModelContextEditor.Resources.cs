using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.SQL.Extensions;
using MCPhappey.Servers.SQL.Repositories;
using MCPhappey.Servers.SQL.Tools.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Servers.SQL.Tools;

public static partial class ModelContextEditor
{
    [Description("Adds a resource to a MCP-server")]
    [McpServerTool(
        Title = "Add a resource to an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_AddResource(
        [Description("Name of the server")]
            string serverName,
        [Description("The URI of the resource to add.")]
            string uri,
        [Description("The name of the resource to add.")]
            string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional title of the resource.")]
        string? title = null,
        [Description("Optional description of the resource.")]
        string? description = null,
        [Description("Optional mimeType. Use 'text/html+skybridge' for app widget resource. Use 'application/vnd.modelcontextprotocol-registry+json' for mcp registries. Use 'application/vnd.agent+json' and 'application/vnd.agents+json' for agent and agent list resources. Use 'application/vnd.conversation+json' and 'application/vnd.conversations+json' for conversation and conversations list resources.")]
        string? mimeType = null,
        [Description("Optional priority of the resource. Between 0 and 1, where 1 is most important and 0 is least important.")]
        float? priority = null,
        [Description("Optional assistant audience target.")]
        bool? assistantAudience = true,
        [Description("Optional user audience target.")]
        bool? userAudience = null,
        CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
            var server = await serviceProvider.GetServer(serverName, cancellationToken);
            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new AddMcpResource()
            {
                Uri = uri,
                Name = name.Slugify().ToLowerInvariant(),
                Title = title,
                MimeType = mimeType,
                Priority = priority.GetPriority(1),
                AssistantAudience = assistantAudience,
                UserAudience = userAudience,
                Description = description
            }, cancellationToken);

            var item = await serverRepository.AddServerResource(server.Id, typed.Uri,
                typed.Name.Slugify().ToLowerInvariant(),
                typed.Description,
                typed.Title,
                typed.MimeType,
                (float?)typed.Priority,
                typed.AssistantAudience,
                typed.UserAudience);

            return item.ToResource();
        }));

    [Description("Updates a resource of a MCP-server")]
    [McpServerTool(
        Title = "Update a resource of an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_UpdateResource(
        [Description("Name of the server")] string serverName,
        [Description("Name of the resource to update")] string resourceName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New value for the uri property")] string? newUri = null,
        [Description("New value for the title property")] string? newTitle = null,
        [Description("New value for the description property")] string? newDescription = null,
        [Description("Optional mimeType.")]
        string? mimeType = null,
        [Description("New value for the priority of the resource. Between 0 and 1, where 1 is most important and 0 is least important.")]
        float? priority = null,
        [Description("New value for the assistant audience target.")]
        bool? assistantAudience = true,
        [Description("New value for the user audience target.")]
        bool? userAudience = null,
        CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
        {
            var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
            var server = await serviceProvider.GetServer(serverName, cancellationToken);
            var resource = server.Resources.FirstOrDefault(a => a.Name == resourceName) ?? throw new ArgumentNullException();
            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(new UpdateMcpResource()
            {
                Description = newDescription ?? resource.Description,
                Title = newTitle ?? resource.Title,
                Name = resource.Name,
                MimeType = mimeType,
                Uri = newUri ?? resource.Uri,
                AssistantAudience = assistantAudience ?? resource.AssistantAudience,
                UserAudience = userAudience ?? resource.UserAudience,
                Priority = (priority ?? resource.Priority).GetPriority(1),
            }, cancellationToken);

            if (!string.IsNullOrEmpty(typed?.Uri))
            {
                resource.Uri = typed.Uri;
            }

            if (!string.IsNullOrEmpty(typed?.Name))
            {
                resource.Name = typed.Name.Slugify().ToLowerInvariant();
            }

            resource.Description = typed?.Description;
            resource.Title = typed?.Title;
            resource.MimeType = typed?.MimeType;
            resource.AssistantAudience = typed?.AssistantAudience;
            resource.UserAudience = typed?.UserAudience;
            resource.Priority = (float?)typed?.Priority;

            var updated = await serverRepository.UpdateResource(resource);

            return updated.ToResource();
        }));

    [Description("Deletes a resource from a MCP-server")]
    [McpServerTool(Title = "Delete a resource from an MCP-server",
        Destructive = true,
        ReadOnly = false,
        Idempotent = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> ModelContextEditor_DeleteResource(
        [Description("Name of the server")] string serverName,
        [Description("Name of the resource to delete")] string resourceName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
           await requestContext.WithExceptionCheck(async () =>
    {
        var serverRepository = serviceProvider.GetRequiredService<ServerRepository>();
        var server = await serviceProvider.GetServer(serverName, cancellationToken);

        return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteResource>(
            expectedName: resourceName,
            deleteAction: async _ =>
            {
                var resource = server.Resources.First(z => z.Name == resourceName);
                await serverRepository.DeleteResource(resource.Id);
            },
            successText: $"Resource {resourceName} has been deleted.",
            ct: cancellationToken);
    });

}

