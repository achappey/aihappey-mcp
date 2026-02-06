using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.ToDo;

public static class GraphToDo
{
    [Description("Add a linked resource to an existing Microsoft To Do task")]
    [McpServerTool(
    Title = "Add Linked Resource to To Do task",
    Destructive = false,
    OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_AddLinkedResource(
    [Description("To Do list id")] string listId,
    [Description("Task id")] string taskId,
    [Description("The external URL to link to the task. Must be HTTPS.")] string webUrl,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("Optional display name for the link.")] string? displayName = null,
    CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (string.IsNullOrWhiteSpace(webUrl) || !webUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("webUrl must be a valid HTTPS URL.", nameof(webUrl));

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                    new GraphNewLinkedResource
                    {
                        Url = webUrl
                    },
                    cancellationToken
                );

            var linkedResource = new LinkedResource
            {
                WebUrl = typed?.Url,
                DisplayName = typed?.DisplayName,
                ExternalId = Guid.NewGuid().ToString()
            };

            return await client.Me.Todo.Lists[listId].Tasks[taskId]
                .LinkedResources
                .PostAsync(linkedResource, cancellationToken: cancellationToken);
        })));

    [Description("Create a new Microsoft To Do task")]
    [McpServerTool(Title = "Create Microsoft To Do task", Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_CreateTask(
     [Description("ToDo list id")] string listId,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("The task title.")] string? title = null,
     [Description("The task content/description.")] string? content = null,
     [Description("The task content type (text or html).")] BodyType? contentType = BodyType.Text,
     [Description("The due date (YYYY-MM-DD).")] DateTime? dueDateTime = null,
     [Description("Importance (low, normal, high).")] Importance? importance = null,
     [Description("Linked resource URLs.")] IEnumerable<string>? linkedResources = null,
     CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewTodoTask
            {
                Title = title ?? string.Empty,
                DueDateTime = dueDateTime,
                Importance = importance,
                Content = content ?? string.Empty,
                ContentType = contentType ?? BodyType.Text
            },
            cancellationToken
        );

        var newTask = await client.Me.Todo.Lists[listId].Tasks
            .PostAsync(new TodoTask
            {
                Title = typed?.Title,
                Importance = typed?.Importance,
                Body = new ItemBody()
                {
                    ContentType = typed?.ContentType,
                    Content = typed?.Content
                },
                DueDateTime = typed?.DueDateTime != null ? new DateTimeTimeZone
                {
                    DateTime = typed.DueDateTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "UTC"
                } : null,
            }, cancellationToken: cancellationToken);

        // Step 2: Add linked resources AFTER task creation
        if (linkedResources != null)
        {
            foreach (var url in linkedResources)
            {
                await client.Me.Todo.Lists[listId].Tasks[newTask!.Id!]
                    .LinkedResources
                    .PostAsync(new LinkedResource
                    {
                        WebUrl = url,
                    }, cancellationToken: cancellationToken);
            }
        }

        return newTask;
    })));

    [Description("Create a new Microsoft To Do task list")]
    [McpServerTool(Title = "Create Microsoft To Do task list",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTodo_CreateTaskList(
     [Description("The task display name.")] string displayName,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewTodoTaskList
            {
                DisplayName = displayName ?? string.Empty,
            },
            cancellationToken
        );

        return await client.Me.Todo.Lists
            .PostAsync(new Microsoft.Graph.Beta.Models.TodoTaskList
            {
                DisplayName = typed?.DisplayName,
            }, cancellationToken: cancellationToken);
    })));

    [Description("Please fill in the To Do task details")]
    public class GraphNewTodoTask
    {
        [JsonPropertyName("title")]
        [Required]
        [Description("The task title.")]
        public string Title { get; set; } = default!;

        [JsonPropertyName("content")]
        [Required]
        [Description("The task content.")]
        public string Content { get; set; } = default!;

        [JsonPropertyName("contentType")]
        [Required]
        [Description("The task content type.")]
        public BodyType ContentType { get; set; } = BodyType.Text;

        [JsonPropertyName("dueDateTime")]
        [Description("The due date (YYYY-MM-DD).")]
        public DateTime? DueDateTime { get; set; }

        [JsonPropertyName("importance")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Importance (low, normal, high).")]
        public Importance? Importance { get; set; }
    }

    [Description("Please fill in the linked resource details")]
    public class GraphNewLinkedResource
    {
        [JsonPropertyName("url")]
        [Required]
        [Description("The linked resource url.")]
        public string Url { get; set; } = default!;

        [JsonPropertyName("displayName")]
        [Description("The linked resource display name.")]
        public string? DisplayName { get; set; }
    }

    [Description("Please fill in the To Do task list details")]
    public class GraphNewTodoTaskList
    {
        [JsonPropertyName("displayName")]
        [Required]
        [Description("The task display name.")]
        public string DisplayName { get; set; } = default!;
    }
}