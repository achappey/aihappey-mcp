using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI302;

public static class AI302PaperWritingPlugin
{
    [Description("Create a 302.AI CoStorm paper-writing task (POST /302/paper/costorm/create).")]
    [McpServerTool(
        Title = "302.AI paper task create",
        Name = "302ai_paper_costorm_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AI302_Paper_CoStorm_Create(
        [Description("JSON object payload for POST /302/paper/costorm/create.")] string payloadJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("If true, poll the task endpoint until completion using the returned task ID.")] bool waitUntilCompleted = false,
        [Description("Polling interval in milliseconds when waitUntilCompleted=true.")] int pollIntervalMs = 2000,
        [Description("Maximum polling attempts when waitUntilCompleted=true.")] int maxPollAttempts = 60,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302PaperCoStormCreateInput
            {
                PayloadJson = payloadJson,
                WaitUntilCompleted = waitUntilCompleted,
                PollIntervalMs = pollIntervalMs,
                MaxPollAttempts = maxPollAttempts
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();
                var body = ParseObject(typed.PayloadJson, "payloadJson");

                var created = await client.PostAsync("302/paper/costorm/create", body, cancellationToken);

                if (!typed.WaitUntilCompleted)
                    return created;

                var taskId = ExtractTaskId(created)
                    ?? throw new Exception("No task_id found in create response; cannot poll for completion.");

                return await PollToCompletionAsync(
                    client,
                    $"302/paper/costorm/tasks/{Uri.EscapeDataString(taskId)}",
                    taskId,
                    typed.PollIntervalMs,
                    typed.MaxPollAttempts,
                    cancellationToken);
            });
        });

    [Description("Get 302.AI CoStorm paper task info (GET /302/paper/costorm/tasks/{task_id}).")]
    [McpServerTool(
        Title = "302.AI paper task status",
        Name = "302ai_paper_costorm_task_status",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Paper_CoStorm_TaskStatus(
        [Description("Task ID returned by /302/paper/costorm/create.")] string taskId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("If true, keep polling until the task reaches a terminal state.")] bool waitUntilCompleted = false,
        [Description("Polling interval in milliseconds when waitUntilCompleted=true.")] int pollIntervalMs = 2000,
        [Description("Maximum polling attempts when waitUntilCompleted=true.")] int maxPollAttempts = 60,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302PaperTaskStatusInput
            {
                TaskId = taskId,
                WaitUntilCompleted = waitUntilCompleted,
                PollIntervalMs = pollIntervalMs,
                MaxPollAttempts = maxPollAttempts
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();
                var path = $"302/paper/costorm/tasks/{Uri.EscapeDataString(typed.TaskId)}";

                if (!typed.WaitUntilCompleted)
                    return await client.GetAsync(path, cancellationToken);

                return await PollToCompletionAsync(
                    client,
                    path,
                    typed.TaskId,
                    typed.PollIntervalMs,
                    typed.MaxPollAttempts,
                    cancellationToken);
            });
        });

    [Description("Create a 302.AI async paper generation task (POST /302/paper/async/chat).")]
    [McpServerTool(
        Title = "302.AI async paper generate",
        Name = "302ai_paper_async_chat",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AI302_Paper_Async_Chat(
        [Description("JSON object payload for POST /302/paper/async/chat.")] string payloadJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("If true, poll async status endpoint until completion using the returned task ID.")] bool waitUntilCompleted = false,
        [Description("Polling interval in milliseconds when waitUntilCompleted=true.")] int pollIntervalMs = 2000,
        [Description("Maximum polling attempts when waitUntilCompleted=true.")] int maxPollAttempts = 60,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302PaperAsyncChatInput
            {
                PayloadJson = payloadJson,
                WaitUntilCompleted = waitUntilCompleted,
                PollIntervalMs = pollIntervalMs,
                MaxPollAttempts = maxPollAttempts
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();
                var body = ParseObject(typed.PayloadJson, "payloadJson");

                var created = await client.PostAsync("302/paper/async/chat", body, cancellationToken);

                if (!typed.WaitUntilCompleted)
                    return created;

                var taskId = ExtractTaskId(created)
                    ?? throw new Exception("No task_id found in async chat response; cannot poll for completion.");

                return await PollToCompletionAsync(
                    client,
                    $"302/paper/async/status/{Uri.EscapeDataString(taskId)}",
                    taskId,
                    typed.PollIntervalMs,
                    typed.MaxPollAttempts,
                    cancellationToken);
            });
        });

    [Description("Get 302.AI async paper generation status/result (GET /302/paper/async/status/{task_id}).")]
    [McpServerTool(
        Title = "302.AI async paper status",
        Name = "302ai_paper_async_status",
        ReadOnly = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> AI302_Paper_Async_Status(
        [Description("Task ID returned by /302/paper/async/chat.")] string taskId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("If true, keep polling until the task reaches a terminal state.")] bool waitUntilCompleted = false,
        [Description("Polling interval in milliseconds when waitUntilCompleted=true.")] int pollIntervalMs = 2000,
        [Description("Maximum polling attempts when waitUntilCompleted=true.")] int maxPollAttempts = 60,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(new AI302PaperTaskStatusInput
            {
                TaskId = taskId,
                WaitUntilCompleted = waitUntilCompleted,
                PollIntervalMs = pollIntervalMs,
                MaxPollAttempts = maxPollAttempts
            }, cancellationToken);

            if (notAccepted != null) return notAccepted;
            if (typed == null) return "Elicitation was not accepted.".ToErrorCallToolResponse();

            return await requestContext.WithStructuredContent(async () =>
            {
                var client = serviceProvider.GetRequiredService<AI302Client>();
                var path = $"302/paper/async/status/{Uri.EscapeDataString(typed.TaskId)}";

                if (!typed.WaitUntilCompleted)
                    return await client.GetAsync(path, cancellationToken);

                return await PollToCompletionAsync(
                    client,
                    path,
                    typed.TaskId,
                    typed.PollIntervalMs,
                    typed.MaxPollAttempts,
                    cancellationToken);
            });
        });

    private static JsonObject ParseObject(string payloadJson, string paramName)
    {
        var node = JsonNode.Parse(payloadJson)
            ?? throw new ArgumentException($"{paramName} is empty or invalid JSON.");

        return node as JsonObject
            ?? throw new ArgumentException($"{paramName} must be a JSON object.");
    }

    private static string? ExtractTaskId(JsonNode? response)
    {
        return response?["task_id"]?.GetValue<string>()
               ?? response?["taskId"]?.GetValue<string>()
               ?? response?["id"]?.GetValue<string>()
               ?? response?["data"]?["task_id"]?.GetValue<string>()
               ?? response?["data"]?["taskId"]?.GetValue<string>()
               ?? response?["result"]?["task_id"]?.GetValue<string>();
    }

    private static async Task<JsonNode?> PollToCompletionAsync(
        AI302Client client,
        string statusPath,
        string taskId,
        int pollIntervalMs,
        int maxPollAttempts,
        CancellationToken cancellationToken)
    {
        var interval = pollIntervalMs < 250 ? 250 : pollIntervalMs;
        var attempts = maxPollAttempts < 1 ? 1 : maxPollAttempts;

        JsonNode? latest = null;

        for (var i = 1; i <= attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            latest = await client.GetAsync(statusPath, cancellationToken);

            if (IsTerminalStatus(latest, out var status, out var failed))
            {
                if (latest is JsonObject latestObj)
                {
                    latestObj["polling"] = new JsonObject
                    {
                        ["task_id"] = taskId,
                        ["attempts"] = i,
                        ["max_attempts"] = attempts,
                        ["interval_ms"] = interval,
                        ["final_status"] = status,
                        ["terminal"] = true,
                        ["failed"] = failed
                    };
                }

                return latest;
            }

            if (i < attempts)
                await Task.Delay(interval, cancellationToken);
        }

        if (latest is JsonObject latestWithTimeout)
        {
            latestWithTimeout["polling"] = new JsonObject
            {
                ["task_id"] = taskId,
                ["attempts"] = attempts,
                ["max_attempts"] = attempts,
                ["interval_ms"] = interval,
                ["terminal"] = false,
                ["timeout"] = true
            };
        }

        return latest;
    }

    private static bool IsTerminalStatus(JsonNode? response, out string status, out bool failed)
    {
        status =
            response?["status"]?.GetValue<string>()
            ?? response?["state"]?.GetValue<string>()
            ?? response?["task_status"]?.GetValue<string>()
            ?? response?["data"]?["status"]?.GetValue<string>()
            ?? response?["data"]?["state"]?.GetValue<string>()
            ?? response?["result"]?["status"]?.GetValue<string>()
            ?? string.Empty;

        var normalized = status.Trim().ToLowerInvariant();

        var isSucceeded = normalized is "success" or "succeeded" or "completed" or "done" or "finish" or "finished";
        var isFailed = normalized is "failed" or "error" or "cancelled" or "canceled" or "timeout";

        failed = isFailed;
        return isSucceeded || isFailed;
    }

    [Description("Please review the CoStorm create request and polling settings.")]
    public class AI302PaperCoStormCreateInput
    {
        [Required]
        [Description("JSON object payload for POST /302/paper/costorm/create.")]
        public string PayloadJson { get; set; } = default!;

        [Description("If true, poll until completion using the returned task ID.")]
        public bool WaitUntilCompleted { get; set; }

        [Range(250, 600000)]
        [Description("Polling interval in milliseconds.")]
        public int PollIntervalMs { get; set; } = 2000;

        [Range(1, 10000)]
        [Description("Maximum polling attempts.")]
        public int MaxPollAttempts { get; set; } = 60;
    }

    [Description("Please review the async chat request and polling settings.")]
    public class AI302PaperAsyncChatInput
    {
        [Required]
        [Description("JSON object payload for POST /302/paper/async/chat.")]
        public string PayloadJson { get; set; } = default!;

        [Description("If true, poll until completion using the returned task ID.")]
        public bool WaitUntilCompleted { get; set; }

        [Range(250, 600000)]
        [Description("Polling interval in milliseconds.")]
        public int PollIntervalMs { get; set; } = 2000;

        [Range(1, 10000)]
        [Description("Maximum polling attempts.")]
        public int MaxPollAttempts { get; set; } = 60;
    }

    [Description("Please review the task status request and polling settings.")]
    public class AI302PaperTaskStatusInput
    {
        [Required]
        [Description("302.AI task ID.")]
        public string TaskId { get; set; } = default!;

        [Description("If true, poll until completion.")]
        public bool WaitUntilCompleted { get; set; }

        [Range(250, 600000)]
        [Description("Polling interval in milliseconds.")]
        public int PollIntervalMs { get; set; } = 2000;

        [Range(1, 10000)]
        [Description("Maximum polling attempts.")]
        public int MaxPollAttempts { get; set; } = 60;
    }
}

