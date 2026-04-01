using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ModelContext.Tasks.Planner;

#pragma warning disable MCPEXP001
public static class PlannerAssignedTasksTools
{
    [Description("Create and assign a new Planner task to the current user. Requires existing groupId and bucketId; no plan or bucket is auto-created.")]
    [McpServerTool(
        Name = "planner_assigned_tasks_create_and_assign",
        Title = "Create and assign Planner task",
        OpenWorld = false,
        Destructive = true,
        TaskSupport = ToolTaskSupport.Optional)]
    public static async Task<CallToolResult?> PlannerAssignedTasks_CreateAndAssign(
        [Description("Microsoft 365 group id that owns the Planner plan.")]
        string groupId,
        [Description("Existing Planner bucket id where the task will be created.")]
        string bucketId,
        [Description("Planner task title.")]
        string title,
        [Description("Optional Planner task description.")]
        string? description,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(groupId)) throw new ArgumentException("groupId is required.");
                if (string.IsNullOrWhiteSpace(bucketId)) throw new ArgumentException("bucketId is required.");
                if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title is required.");

                var http = await serviceProvider.GetGraphHttpClient(requestContext.Server);

                var me = await GetJsonAsync(http, "me?$select=id", cancellationToken);
                var myUserId = me["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(myUserId))
                    throw new InvalidOperationException("Could not resolve current user id from Microsoft Graph /me.");

                var bucket = await GetJsonAsync(http, $"planner/buckets/{Uri.EscapeDataString(bucketId)}?$select=id,planId", cancellationToken);
                var planId = bucket["planId"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(planId))
                    throw new InvalidOperationException("Could not resolve planId for the provided bucketId.");

                var plan = await GetJsonAsync(http, $"planner/plans/{Uri.EscapeDataString(planId)}?$select=id,owner", cancellationToken);
                var owner = plan["owner"]?.GetValue<string>();
                if (!string.Equals(owner, groupId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("bucketId does not belong to a Planner plan owned by the provided groupId.");

                var createBody = new JsonObject
                {
                    ["planId"] = planId,
                    ["bucketId"] = bucketId,
                    ["title"] = title,
                    ["assignments"] = new JsonObject
                    {
                        [myUserId] = new JsonObject
                        {
                            ["@odata.type"] = "microsoft.graph.plannerAssignment",
                            ["orderHint"] = " !"
                        }
                    }
                };

                var createdTask = await PostJsonAsync(http, "planner/tasks", createBody, cancellationToken);
                var createdTaskId = createdTask["id"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Microsoft Graph did not return a Planner task id.");

                if (!string.IsNullOrWhiteSpace(description))
                {
                    await PatchTaskDetailsDescriptionAsync(http, createdTaskId, description!, cancellationToken);
                }

                return new PlannerAssignedTaskCreateResult
                {
                    PlannerTaskId = createdTaskId,
                    GroupId = groupId,
                    PlanId = planId,
                    BucketId = bucketId,
                    AssignedToUserId = myUserId,
                    Title = createdTask["title"]?.GetValue<string>() ?? title,
                    Description = description,
                    PercentComplete = createdTask["percentComplete"]?.GetValue<int>() ?? 0,
                    CreatedDateTime = createdTask["createdDateTime"]?.GetValue<string>()
                };
            }));

    private static async Task<JsonObject> GetJsonAsync(HttpClient http, string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(relativeUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new IOException($"Graph GET '{relativeUrl}' failed ({(int)response.StatusCode}): {error}");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(payload) as JsonObject
            ?? throw new IOException($"Graph GET '{relativeUrl}' returned invalid JSON.");
    }

    private static async Task<JsonObject> PostJsonAsync(HttpClient http, string relativeUrl, JsonObject body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(
            relativeUrl,
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new IOException($"Graph POST '{relativeUrl}' failed ({(int)response.StatusCode}): {error}");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(payload) as JsonObject
            ?? throw new IOException($"Graph POST '{relativeUrl}' returned invalid JSON.");
    }

    private static async Task PatchTaskDetailsDescriptionAsync(
        HttpClient http,
        string taskId,
        string description,
        CancellationToken cancellationToken)
    {
        var detailsUrl = $"planner/tasks/{Uri.EscapeDataString(taskId)}/details";
        using var details = await http.GetAsync(detailsUrl, cancellationToken);
        if (!details.IsSuccessStatusCode)
        {
            var detailsError = await details.Content.ReadAsStringAsync(cancellationToken);
            throw new IOException($"Graph GET '{detailsUrl}' failed ({(int)details.StatusCode}): {detailsError}");
        }

        var etag = details.Headers.ETag?.Tag;
        if (string.IsNullOrWhiteSpace(etag))
            throw new IOException("Missing ETag for Planner task details update.");

        var patchBody = new JsonObject
        {
            ["description"] = description,
            ["previewType"] = "description"
        };

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, detailsUrl)
        {
            Content = new StringContent(patchBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);

        using var patchResponse = await http.SendAsync(patchRequest, cancellationToken);
        if (patchResponse.StatusCode is not HttpStatusCode.NoContent and not HttpStatusCode.OK)
        {
            var patchError = await patchResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new IOException($"Graph PATCH '{detailsUrl}' failed ({(int)patchResponse.StatusCode}): {patchError}");
        }
    }

    public sealed class PlannerAssignedTaskCreateResult
    {
        public string PlannerTaskId { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string PlanId { get; set; } = string.Empty;
        public string BucketId { get; set; } = string.Empty;
        public string AssignedToUserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int PercentComplete { get; set; }
        public string? CreatedDateTime { get; set; }
    }
}
#pragma warning restore MCPEXP001
