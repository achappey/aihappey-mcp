using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Constants;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tools.ModelContext.Tasks.Planner;

#pragma warning disable MCPEXP001
public sealed class PlannerAssignedTasksReadThroughProvider(IServiceScopeFactory scopeFactory) : IExternalTaskRuntimeProvider
{
    public IMcpTaskStore CreateTaskStore(ExternalTaskRuntimeContext runtimeContext)
        => new PlannerAssignedTasksTaskStore(scopeFactory, runtimeContext);

    public void TryMutateInitializeResult(JsonRpcResponse response, ExternalTaskRuntimeContext runtimeContext)
    {
        if (response.Result is not JsonObject root ||
            root["capabilities"] is not JsonObject capabilities ||
            root["serverInfo"] is null ||
            root["protocolVersion"] is null)
        {
            return;
        }

        capabilities["tasks"] = new JsonObject
        {
            ["list"] = new JsonObject()
        };
    }
}

internal sealed class PlannerAssignedTasksTaskStore(
    IServiceScopeFactory scopeFactory,
    ExternalTaskRuntimeContext runtimeContext) : IMcpTaskStore
{
    public Task<McpTask> CreateTaskAsync(
        McpTaskMetadata taskParams,
        RequestId requestId,
        JsonRpcRequest request,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("CreateTaskAsync is not supported for planner read-through task runtime.");

    public async Task<McpTask?> GetTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        var response = await ExecuteGraphRequestAsync(
            async http => await http.GetAsync(
                $"planner/tasks/{Uri.EscapeDataString(taskId)}?$select=id,title,percentComplete,createdDateTime,completedDateTime",
                cancellationToken),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new McpProtocolException("Failed to retrieve task state.", McpErrorCode.InternalError);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(payload) is not JsonObject taskJson)
        {
            throw new McpProtocolException("Task payload is invalid.", McpErrorCode.InternalError);
        }

        return ToPlannerTask(taskJson, runtimeContext.PollInterval);
    }

    public Task<McpTask> StoreTaskResultAsync(
        string taskId,
        McpTaskStatus status,
        JsonElement result,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("StoreTaskResultAsync is not supported for planner read-through task runtime.");

    public async Task<JsonElement> GetTaskResultAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new McpProtocolException("Missing required parameter 'taskId'", McpErrorCode.InvalidParams);
        }

        var taskResponse = await ExecuteGraphRequestAsync(
            async http => await http.GetAsync(
                $"planner/tasks/{Uri.EscapeDataString(taskId)}?$select=id,title,percentComplete,createdDateTime,completedDateTime",
                cancellationToken),
            cancellationToken);

        if (taskResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new McpProtocolException($"Task not found: '{taskId}'", McpErrorCode.InvalidParams);
        }

        if (!taskResponse.IsSuccessStatusCode)
        {
            throw new McpProtocolException("Failed to retrieve task state.", McpErrorCode.InternalError);
        }

        var taskPayload = await taskResponse.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(taskPayload) is not JsonObject taskJson)
        {
            throw new McpProtocolException("Task payload is invalid.", McpErrorCode.InternalError);
        }

        var details = await TryGetTaskDetailsAsync(taskId, cancellationToken);
        var resultNode = new JsonObject
        {
            ["task"] = taskJson,
            ["details"] = details
        };

        using var doc = JsonDocument.Parse(resultNode.ToJsonString());
        return doc.RootElement.Clone();
    }

    public Task<McpTask> UpdateTaskStatusAsync(
        string taskId,
        McpTaskStatus status,
        string? statusMessage,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("UpdateTaskStatusAsync is not supported for planner read-through task runtime.");

    public async Task<ListTasksResult> ListTasksAsync(
        string? cursor = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteGraphRequestAsync(
            async http =>
            {
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return await http.GetAsync(
                        "me/planner/tasks?$top=50&$select=id,title,percentComplete,createdDateTime,completedDateTime",
                        cancellationToken);
                }

                if (!Uri.TryCreate(cursor, UriKind.Absolute, out var cursorUri))
                {
                    throw new McpProtocolException("Invalid cursor.", McpErrorCode.InvalidParams);
                }

                return await http.GetAsync(cursorUri, cancellationToken);
            },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new McpProtocolException("Invalid cursor.", McpErrorCode.InvalidParams);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new McpProtocolException("Failed to list tasks.", McpErrorCode.InternalError);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(payload) is not JsonObject root)
        {
            throw new McpProtocolException("Task list payload is invalid.", McpErrorCode.InternalError);
        }

        var tasks = new List<McpTask>();
        if (root["value"] is JsonArray value)
        {
            foreach (var item in value.OfType<JsonObject>())
            {
                tasks.Add(ToPlannerTask(item, runtimeContext.PollInterval));
            }
        }

        return new ListTasksResult
        {
            Tasks = [.. tasks],
            NextCursor = root["@odata.nextLink"]?.GetValue<string>()
        };
    }

    public Task<McpTask> CancelTaskAsync(string taskId, string? sessionId = null, CancellationToken cancellationToken = default)
        => throw new McpProtocolException("Method 'tasks/cancel' is not available.", McpErrorCode.MethodNotFound);

    public void Dispose()
    {
    }

    private async Task<HttpResponseMessage> ExecuteGraphRequestAsync(
        Func<HttpClient, Task<HttpResponseMessage>> request,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.WithHeaders(runtimeContext.Headers);

        var client = await CreateGraphHttpClientAsync(services);
        return await request(client);
    }

    private async Task<HttpClient> CreateGraphHttpClientAsync(IServiceProvider services)
    {
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var headerProvider = services.GetRequiredService<HeaderProvider>();
        var oAuthSettings = services.GetRequiredService<OAuthSettings>();

        var accessToken = await httpClientFactory.GetOboToken(
            headerProvider.Bearer!,
            Hosts.MicrosoftGraph,
            runtimeContext.ServerConfig.Server,
            oAuthSettings);

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        client.BaseAddress = new Uri("https://graph.microsoft.com/beta/");
        return client;
    }

    private async Task<JsonObject?> TryGetTaskDetailsAsync(string taskId, CancellationToken cancellationToken)
    {
        var response = await ExecuteGraphRequestAsync(
            async http => await http.GetAsync(
                $"planner/tasks/{Uri.EscapeDataString(taskId)}/details?$select=description",
                cancellationToken),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(payload) as JsonObject;
    }

    private static McpTask ToPlannerTask(JsonObject taskJson, TimeSpan pollInterval)
    {
        var taskId = taskJson["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new McpProtocolException("Task payload missing id.", McpErrorCode.InternalError);
        }

        var createdAt = ParseOffset(taskJson["createdDateTime"]) ?? DateTimeOffset.UtcNow;
        var completedAt = ParseOffset(taskJson["completedDateTime"]);
        var percent = taskJson["percentComplete"]?.GetValue<int>() ?? 0;

        return new McpTask
        {
            TaskId = taskId,
            Status = percent >= 100 ? McpTaskStatus.Completed : McpTaskStatus.Working,
            StatusMessage = taskJson["title"]?.GetValue<string>(),
            CreatedAt = createdAt,
            LastUpdatedAt = completedAt ?? createdAt,
            TimeToLive = TimeSpan.MaxValue,
            PollInterval = pollInterval
        };
    }

    private static DateTimeOffset? ParseOffset(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text)
            && DateTimeOffset.TryParse(text, out var offset))
        {
            return offset;
        }

        return null;
    }
}
#pragma warning restore MCPEXP001
