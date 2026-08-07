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
    [Description("Create a checklist item on an existing Microsoft To Do task.")]
    [McpServerTool(Title = "Create To Do checklist item", Name = "graph_todo_create_checklist_item",
        UseStructuredContent = true, OutputSchemaType = typeof(ChecklistItem), Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_CreateChecklistItem(
        [Description("To Do list ID.")] string listId,
        [Description("To Do task ID.")] string taskId,
        [Description("Checklist item display name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Whether the checklist item is completed.")] bool isChecked = false,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphChecklistItemInput { DisplayName = displayName, IsChecked = isChecked }, cancellationToken);
            if (notAccepted is not null) return default(ChecklistItem);
            ArgumentException.ThrowIfNullOrWhiteSpace(typed?.DisplayName);

            return await client.Me.Todo.Lists[listId].Tasks[taskId].ChecklistItems.PostAsync(
                new ChecklistItem { DisplayName = typed.DisplayName.Trim(), IsChecked = typed.IsChecked },
                cancellationToken: cancellationToken);
        })));

    [Description("Rename or complete/reopen a checklist item on a Microsoft To Do task.")]
    [McpServerTool(Title = "Update To Do checklist item", Name = "graph_todo_update_checklist_item",
        UseStructuredContent = true, OutputSchemaType = typeof(ChecklistItem), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_UpdateChecklistItem(
        [Description("To Do list ID.")] string listId,
        [Description("To Do task ID.")] string taskId,
        [Description("Checklist item ID.")] string checklistItemId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated display name. Leave empty to keep unchanged.")] string? displayName = null,
        [Description("Updated completion state. Leave empty to keep unchanged.")] bool? isChecked = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && isChecked is null)
                throw new ValidationException("displayName or isChecked must be provided.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphChecklistItemUpdate { DisplayName = displayName, IsChecked = isChecked }, cancellationToken);
            if (notAccepted is not null) return default(ChecklistItem);
            if (typed?.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(typed.DisplayName);

            return await client.Me.Todo.Lists[listId].Tasks[taskId].ChecklistItems[checklistItemId].PatchAsync(
                new ChecklistItem { DisplayName = typed?.DisplayName?.Trim(), IsChecked = typed?.IsChecked },
                cancellationToken: cancellationToken);
        })));

    [Description("Delete a checklist item from a Microsoft To Do task.")]
    [McpServerTool(Title = "Delete To Do checklist item", Name = "graph_todo_delete_checklist_item",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_DeleteChecklistItem(
        [Description("To Do list ID.")] string listId,
        [Description("To Do task ID.")] string taskId,
        [Description("Checklist item ID to delete.")] string checklistItemId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteChecklistItem>(
            checklistItemId,
            async _ => await client.Me.Todo.Lists[listId].Tasks[taskId].ChecklistItems[checklistItemId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "To Do checklist item deleted.", cancellationToken)));

    [Description("Please fill in the Microsoft To Do checklist item details.")]
    public sealed class GraphChecklistItemInput
    {
        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = default!;

        [JsonPropertyName("isChecked")]
        public bool IsChecked { get; set; }
    }

    [Description("Please fill in the Microsoft To Do checklist item fields to update.")]
    public sealed class GraphChecklistItemUpdate
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isChecked")]
        public bool? IsChecked { get; set; }
    }

    [Description("Please confirm the To Do checklist item ID to delete: {0}")]
    public sealed class GraphDeleteChecklistItem : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
