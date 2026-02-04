using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.ToDo;

public static class TodoTaskList
{
    private const string ListUrlTemplate = "https://graph.microsoft.com/beta/me/todo/lists/{0}";

    [Description("Create a new To Do Task List.")]
    [McpServerTool(
        Title = "Create To Do Task List",
        Name = "todo_task_list_create",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> TodoTaskList_CreateList(
        [Description("Title of the new todo list")]
        string listTitle,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new TodoTaskListNewList
            {
                Title = listTitle
            },
            cancellationToken);

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));
        if (typed == null) throw new Exception("Invalid result");

        var list = await client.Me.Todo.Lists.PostAsync(new Microsoft.Graph.Beta.Models.TodoTaskList
        {
            DisplayName = typed.Title
        }, cancellationToken: cancellationToken);

        var result = new TodoTaskListListResult
        {
            ListId = list?.Id ?? string.Empty,
            Title = list?.DisplayName ?? typed.Title
        };

        return result.ToJsonContentBlock(string.Format(ListUrlTemplate, list?.Id))
            .ToCallToolResult();
    })));

    [Description("Add a todo item to a To Do Task List.")]
    [McpServerTool(
        Title = "Add To Do Task List item",
        Name = "todo_task_list_add_item",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> TodoTaskList_AddItem(
        [Description("To Do list id")]
        string listId,
        [Description("Todo title")]
        string title,
        [Description("Todo description")]
        string? description,
        [Description("Whether the todo is completed")]
        bool completed,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var newTask = await client.Me.Todo.Lists[listId].Tasks.PostAsync(new TodoTask
        {
            Title = title,
            Status = completed ? Microsoft.Graph.Beta.Models.TaskStatus.Completed : Microsoft.Graph.Beta.Models.TaskStatus.NotStarted,
            Body = string.IsNullOrWhiteSpace(description)
                ? null
                : new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = description
                }
        }, cancellationToken: cancellationToken);

        var result = new TodoTaskListItemResult
        {
            Title = newTask?.Title ?? title,
            Description = description ?? string.Empty,
            Completed = completed
        };

        return result.ToJsonContentBlock(string.Format(ListUrlTemplate, listId))
            .ToCallToolResult();
    })));

    [Description("Complete all todo items in a To Do Task List that match the given title (case-insensitive).")]
    [McpServerTool(
        Title = "Complete To Do Task List items by title",
        Name = "todo_task_list_complete_by_title",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> TodoTaskList_CompleteByTitle(
        [Description("To Do list id")]
        string listId,
        [Description("Todo title to complete")]
        string title,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var tasks = await client.Me.Todo.Lists[listId].Tasks.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["id", "title", "status"];
            requestConfiguration.QueryParameters.Top = 200;
        }, cancellationToken);

        var matches = tasks?.Value?
            .Where(t => string.Equals(t.Title, title, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        var completedCount = 0;
        foreach (var task in matches)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
                continue;

            if (task.Status == Microsoft.Graph.Beta.Models.TaskStatus.Completed)
            {
                completedCount++;
                continue;
            }

            await client.Me.Todo.Lists[listId].Tasks[task.Id].PatchAsync(new TodoTask
            {
                Status = Microsoft.Graph.Beta.Models.TaskStatus.Completed
            }, cancellationToken: cancellationToken);

            completedCount++;
        }

        var result = new TodoTaskListCompleteResult
        {
            Title = title,
            CompletedCount = completedCount
        };

        return result.ToJsonContentBlock(string.Format(ListUrlTemplate, listId))
            .ToCallToolResult();
    })));

    [Description("List all todo items in a To Do Task List. Returns only title, description, and completed.")]
    [McpServerTool(
        Title = "List To Do Task List items",
        Name = "todo_task_list_list_items",
        OpenWorld = false,
        ReadOnly = true,
        Destructive = false)]
    public static async Task<CallToolResult?> TodoTaskList_ListItems(
        [Description("To Do list id")]
        string listId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var tasks = await client.Me.Todo.Lists[listId].Tasks.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["title", "status", "body"];
            requestConfiguration.QueryParameters.Top = 200;
        }, cancellationToken);

        var results = new List<TodoTaskListItemResult>();
        foreach (var task in tasks?.Value ?? [])
        {
            results.Add(new TodoTaskListItemResult
            {
                Title = task.Title ?? string.Empty,
                Description = task.Body?.Content ?? string.Empty,
                Completed = task.Status == Microsoft.Graph.Beta.Models.TaskStatus.Completed
            });
        }

        return results.ToJsonContentBlock(string.Format(ListUrlTemplate, listId))
            .ToCallToolResult();
    })));

    [Description("Please fill in the To Do Task list details")]
    public class TodoTaskListNewList
    {
        [JsonPropertyName("title")]
        [Required]
        [Description("Name of the new To Do Task list.")]
        public string Title { get; set; } = default!;
    }

    public class TodoTaskListListResult
    {
        public string ListId { get; set; } = default!;
        public string Title { get; set; } = default!;
    }

    public class TodoTaskListItemResult
    {
        public string Title { get; set; } = default!;
        public string Description { get; set; } = default!;
        public bool Completed { get; set; }
    }

    public class TodoTaskListCompleteResult
    {
        public string Title { get; set; } = default!;
        public int CompletedCount { get; set; }
    }
}
