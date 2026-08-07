using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.ToDo;

public static partial class GraphToDo
{
    [Description("Update a linked resource on an existing Microsoft To Do task.")]
    [McpServerTool(Title = "Update To Do linked resource",
        Name = "graph_todo_update_linked_resource",
        UseStructuredContent = true,
        OutputSchemaType = typeof(LinkedResource),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_UpdateLinkedResource(
        [Description("To Do list ID.")] string listId,
        [Description("To Do task ID.")] string taskId,
        [Description("Linked resource ID.")] string linkedResourceId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement HTTPS URL. Leave empty to keep unchanged.")] string? webUrl = null,
        [Description("Replacement display name. Leave empty to keep unchanged.")] string? displayName = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (webUrl is null && displayName is null)
                throw new ValidationException("webUrl or displayName must be provided.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateLinkedResource { WebUrl = webUrl, DisplayName = displayName }, cancellationToken);
            if (notAccepted is not null || input is null) return default(LinkedResource);
            if (input.WebUrl is not null && !input.WebUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("webUrl must be a valid HTTPS URL.");

            return await client.Me.Todo.Lists[listId].Tasks[taskId].LinkedResources[linkedResourceId]
                .PatchAsync(new LinkedResource
                {
                    WebUrl = input.WebUrl,
                    DisplayName = input.DisplayName
                }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a linked resource from an existing Microsoft To Do task.")]
    [McpServerTool(Title = "Delete To Do linked resource",
        Name = "graph_todo_delete_linked_resource",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_DeleteLinkedResource(
        [Description("To Do list ID.")] string listId,
        [Description("To Do task ID.")] string taskId,
        [Description("Linked resource ID to delete.")] string linkedResourceId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteLinkedResource>(
            linkedResourceId,
            async _ => await client.Me.Todo.Lists[listId].Tasks[taskId].LinkedResources[linkedResourceId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "To Do linked resource deleted.",
            cancellationToken)));

    [Description("Please review the linked resource fields to update.")]
    public sealed class GraphUpdateLinkedResource
    {
        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    [Description("Please confirm the To Do linked resource ID to delete: {0}")]
    public sealed class GraphDeleteLinkedResource : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
