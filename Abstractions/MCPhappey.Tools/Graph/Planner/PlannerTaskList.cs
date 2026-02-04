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

namespace MCPhappey.Tools.Graph.Planner;

public static class PlannerTaskList
{
    private const string DefaultBucketName = "Tasks";

    [Description("Create a new Planner Task List list (Planner plan) with a default Tasks bucket.")]
    [McpServerTool(
        Title = "Create Planner Task List",
        Name = "planner_task_list_create",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> PlannerTodos_CreateList(
        [Description("Group id (Microsoft 365 group that will own the plan)")]
        string groupId,
        [Description("Title of the new todo list")]
        string listTitle,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new PlannerTodosNewList
            {
                Title = listTitle
            },
            cancellationToken);

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));
        if (typed == null) throw new Exception("Invalid result");

        _ = await client.Groups[groupId]
            .GetAsync(cancellationToken: cancellationToken);

        var plan = await client.Planner.Plans.PostAsync(new PlannerPlan
        {
            Title = typed.Title,
            Owner = groupId
        }, cancellationToken: cancellationToken);

        var bucket = await client.Planner.Buckets.PostAsync(new PlannerBucket
        {
            Name = DefaultBucketName,
            PlanId = plan?.Id
        }, cancellationToken: cancellationToken);

        var result = new PlannerTodosListResult
        {
            PlanId = plan?.Id ?? string.Empty,
            Title = plan?.Title ?? typed.Title,
            BucketName = bucket?.Name ?? DefaultBucketName
        };

        return result.ToJsonContentBlock($"https://graph.microsoft.com/beta/planner/plans/{plan?.Id}")
            .ToCallToolResult();
    })));

    [Description("Add a todo item to a Planner Task List. Creates the default Tasks bucket if it is missing.")]
    [McpServerTool(
        Title = "Add Planner Task List item",
        Name = "planner_task_list_add_item",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> PlannerTodos_AddItem(
        [Description("Planner list id (plan id)")]
        string planId,
        [Description("Todo title")]
        string title,
        [Description("Todo description")]
        string? description,
        [Description("Whether the todo is completed")]
        bool completed,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var bucket = await GetOrCreateTasksBucketAsync(client, planId, cancellationToken);

        var newTask = await client.Planner.Tasks.PostAsync(new PlannerTask
        {
            Title = title,
            PlanId = planId,
            BucketId = bucket.Id,
            PercentComplete = completed ? 100 : 0
        }, cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(newTask?.Id))
        {
            await UpdateTaskDescriptionAsync(serviceProvider, requestContext, newTask.Id!, description!, cancellationToken);
        }

        var result = new PlannerTodosItemResult
        {
            Title = newTask?.Title ?? title,
            Description = description ?? string.Empty,
            Completed = completed
        };

        return result.ToJsonContentBlock($"https://graph.microsoft.com/beta/planner/plans/{planId}")
            .ToCallToolResult();
    })));

    [Description("Complete all todo items in a Planner Task List that match the given title (case-insensitive).")]
    [McpServerTool(
        Title = "Complete Planner Task List items by title",
        Name = "planner_task_list_complete_by_title",
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> PlannerTodos_CompleteByTitle(
        [Description("Planner list id (plan id)")]
        string planId,
        [Description("Todo title to complete")]
        string title,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var tasks = await client.Planner.Plans[planId].Tasks.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["id", "title", "percentComplete"];
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

            await SetTaskPercentCompleteAsync(serviceProvider, requestContext, task.Id!, 100, cancellationToken);
            completedCount++;
        }

        var result = new PlannerTodosCompleteResult
        {
            Title = title,
            CompletedCount = completedCount
        };

        return result.ToJsonContentBlock($"https://graph.microsoft.com/beta/planner/plans/{planId}")
            .ToCallToolResult();
    })));

    [Description("List all todo items in a Planner Task List. Returns only title, description, and completed.")]
    [McpServerTool(
        Title = "List Planner Task List items",
        Name = "planner_task_list_list_items",
        OpenWorld = false,
        ReadOnly = true,
        Destructive = false)]
    public static async Task<CallToolResult?> PlannerTodos_ListItems(
        [Description("Planner list id (plan id)")]
        string planId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var tasks = await client.Planner.Plans[planId].Tasks.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["id", "title", "percentComplete", "hasDescription"];
            requestConfiguration.QueryParameters.Top = 200;
        }, cancellationToken);

        var results = new List<PlannerTodosItemResult>();
        foreach (var task in tasks?.Value ?? [])
        {
            var description = string.Empty;
            if (task.HasDescription == true && !string.IsNullOrWhiteSpace(task.Id))
            {
                description = await GetTaskDescriptionAsync(serviceProvider, requestContext, task.Id!, cancellationToken);
            }

            results.Add(new PlannerTodosItemResult
            {
                Title = task.Title ?? string.Empty,
                Description = description,
                Completed = (task.PercentComplete ?? 0) >= 100
            });
        }

        return results.ToJsonContentBlock($"https://graph.microsoft.com/beta/planner/plans/{planId}")
            .ToCallToolResult();
    })));

    private static async Task<PlannerBucket> GetOrCreateTasksBucketAsync(
        Microsoft.Graph.Beta.GraphServiceClient client,
        string planId,
        CancellationToken cancellationToken)
    {
        var buckets = await client.Planner.Plans[planId].Buckets.GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = ["id", "name"];
            requestConfiguration.QueryParameters.Top = 100;
        }, cancellationToken);

        var existing = buckets?.Value?
            .FirstOrDefault(b => string.Equals(b.Name, DefaultBucketName, StringComparison.OrdinalIgnoreCase));

        if (existing?.Id != null)
            return existing;

        return await client.Planner.Buckets.PostAsync(new PlannerBucket
        {
            Name = DefaultBucketName,
            PlanId = planId
        }, cancellationToken: cancellationToken) ?? throw new Exception("Could not create default bucket.");
    }

    private static async Task UpdateTaskDescriptionAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string taskId,
        string description,
        CancellationToken cancellationToken)
    {
        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

        var detailsUrl = $"planner/tasks/{taskId}/details";
        var detailsResp = await httpClient.GetAsync(detailsUrl, cancellationToken);
        detailsResp.EnsureSuccessStatusCode();
        var etag = detailsResp.Headers.ETag?.Tag ?? "*";

        var patch = new
        {
            description,
            previewType = "description"
        };

        var patchContent = new StringContent(
            JsonSerializer.Serialize(patch),
            System.Text.Encoding.UTF8,
            "application/json");

        var patchReq = new HttpRequestMessage(HttpMethod.Patch, detailsUrl)
        {
            Content = patchContent
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", etag);

        var patchResp = await httpClient.SendAsync(patchReq, cancellationToken);
        patchResp.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetTaskDescriptionAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string taskId,
        CancellationToken cancellationToken)
    {
        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        var detailsUrl = $"planner/tasks/{taskId}/details?$select=description";
        var detailsResp = await httpClient.GetAsync(detailsUrl, cancellationToken);
        detailsResp.EnsureSuccessStatusCode();

        var payload = await detailsResp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("description", out var desc))
        {
            return desc.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task SetTaskPercentCompleteAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string taskId,
        int percentComplete,
        CancellationToken cancellationToken)
    {
        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

        var taskUrl = $"planner/tasks/{taskId}";
        var taskResp = await httpClient.GetAsync(taskUrl, cancellationToken);
        taskResp.EnsureSuccessStatusCode();
        var etag = taskResp.Headers.ETag?.Tag ?? "*";

        var patch = new
        {
            percentComplete
        };

        var patchContent = new StringContent(
            JsonSerializer.Serialize(patch),
            System.Text.Encoding.UTF8,
            "application/json");

        var patchReq = new HttpRequestMessage(HttpMethod.Patch, taskUrl)
        {
            Content = patchContent
        };
        patchReq.Headers.TryAddWithoutValidation("If-Match", etag);

        var patchResp = await httpClient.SendAsync(patchReq, cancellationToken);
        patchResp.EnsureSuccessStatusCode();
    }

    [Description("Please fill in the Planner Todo list details")]
    public class PlannerTodosNewList
    {
        [JsonPropertyName("title")]
        [Required]
        [Description("Name of the new Planner Todo list.")]
        public string Title { get; set; } = default!;
    }

    public class PlannerTodosListResult
    {
        public string PlanId { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string BucketName { get; set; } = default!;
    }

    public class PlannerTodosItemResult
    {
        public string Title { get; set; } = default!;
        public string Description { get; set; } = default!;
        public bool Completed { get; set; }
    }

    public class PlannerTodosCompleteResult
    {
        public string Title { get; set; } = default!;
        public int CompletedCount { get; set; }
    }
}
