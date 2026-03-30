using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Smooth;

public static class SmoothTaskTools
{
    [Description("Submit a Smooth task, poll until completion, send progress notifications, upload resulting files, and return resource links with structured content.")]
    [McpServerTool(
        Title = "Smooth submit task and wait",
        Name = "smooth_task_submit_and_wait",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Smooth_Task_Submit_And_Wait(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Task instruction for Smooth. Ignored when openSessionOnly=true.")]
        string? task = null,
        [Description("When true, sends task=null and opens a browser session.")]
        bool openSessionOnly = false,
        [Description("Optional start URL.")]
        string? url = null,
        [Description("Optional response model JSON schema as string.")]
        string? responseModelJson = null,
        [Description("Optional metadata JSON object as string.")]
        string? metadataJson = null,
        [Description("Optional files JSON array of file IDs as string.")]
        string? filesJson = null,
        [Description("Agent: smooth or smooth-lite.")]
        string agent = "smooth",
        [Description("Maximum number of steps.")][Range(2, 128)]
        int maxSteps = 32,
        [Description("Device: desktop, mobile, desktop-lg.")]
        string device = "desktop",
        [Description("Optional allowed_urls JSON array.")]
        string? allowedUrlsJson = null,
        [Description("Enable recording.")]
        bool enableRecording = true,
        [Description("Optional profile id.")]
        string? profileId = null,
        [Description("Run profile in read-only mode.")]
        bool profileReadOnly = false,
        [Description("Enable stealth mode.")]
        bool stealthMode = false,
        [Description("Optional proxy server.")]
        string? proxyServer = null,
        [Description("Optional proxy username.")]
        string? proxyUsername = null,
        [Description("Optional proxy password.")]
        string? proxyPassword = null,
        [Description("Optional use_adblock value.")]
        bool? useAdblock = null,
        [Description("Optional additional_tools JSON object.")]
        string? additionalToolsJson = null,
        [Description("Optional extensions JSON array.")]
        string? extensionsJson = null,
        [Description("Show mouse cursor.")]
        bool showCursor = false,
        [Description("Polling interval in seconds.")][Range(1, 60)]
        int pollIntervalSeconds = 2,
        [Description("Maximum wait time in seconds.")][Range(10, 3600)]
        int maxWaitSeconds = 600,
        [Description("Include downloads URL during polling.")]
        bool includeDownloads = true,
        [Description("Base output filename for uploaded artifacts.")]
        string? filename = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            if (!openSessionOnly && string.IsNullOrWhiteSpace(task))
                throw new ValidationException("task is required when openSessionOnly is false.");

            if (!string.Equals(agent, "smooth", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(agent, "smooth-lite", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("agent must be 'smooth' or 'smooth-lite'.");

            var payload = BuildPayload(
                task,
                openSessionOnly,
                url,
                responseModelJson,
                metadataJson,
                filesJson,
                agent,
                maxSteps,
                device,
                allowedUrlsJson,
                enableRecording,
                profileId,
                profileReadOnly,
                stealthMode,
                proxyServer,
                proxyUsername,
                proxyPassword,
                useAdblock,
                additionalToolsJson,
                extensionsJson,
                showCursor);

            var smoothClient = serviceProvider.GetRequiredService<SmoothClient>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            using var client = serviceProvider.CreateSmoothClient();

            await requestContext.Server.SendMessageNotificationAsync("Submitting Smooth task.", LoggingLevel.Info, cancellationToken);
            var submitResponse = await smoothClient.SubmitTaskAsync(client, payload, cancellationToken);
            var currentTask = submitResponse["r"] as JsonObject ?? throw new InvalidOperationException("Smooth response missing 'r'.");

            var taskId = currentTask["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(taskId))
                throw new InvalidOperationException("Smooth response missing task id.");

            var status = currentTask["status"]?.GetValue<string>() ?? "unknown";
            await requestContext.Server.SendMessageNotificationAsync($"Smooth task submitted: {taskId} (status={status}).", LoggingLevel.Info, cancellationToken);

            var eventTimestamp = GetLastEventTimestamp(currentTask);
            int? progressCounter = 0;
            var startedAt = DateTimeOffset.UtcNow;

            while (IsRunningStatus(status))
            {
                var elapsed = DateTimeOffset.UtcNow - startedAt;
                if (elapsed > TimeSpan.FromSeconds(maxWaitSeconds))
                    throw new TimeoutException($"Smooth task {taskId} did not complete within {maxWaitSeconds} seconds.");

                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken);

                var pollResponse = await smoothClient.GetTaskAsync(
                    client,
                    taskId,
                    eventTimestamp,
                    includeDownloads,
                    cancellationToken);

                currentTask = pollResponse["r"] as JsonObject ?? throw new InvalidOperationException("Smooth polling response missing 'r'.");
                status = currentTask["status"]?.GetValue<string>() ?? "unknown";

                if (currentTask["events"] is JsonArray events)
                {
                    foreach (var evt in events.OfType<JsonObject>())
                    {
                        var name = evt["name"]?.GetValue<string>() ?? "event";
                        var payloadText = evt["payload"]?.ToJsonString() ?? "{}";
                        await requestContext.Server.SendMessageNotificationAsync(
                            $"Smooth event [{name}] {payloadText}",
                            LoggingLevel.Info,
                            cancellationToken);

                        var ts = evt["timestamp"]?.GetValue<long?>();
                        if (ts.HasValue && ts.Value > eventTimestamp)
                            eventTimestamp = ts.Value;
                    }
                }

                progressCounter = await requestContext.Server.SendProgressNotificationAsync(
                    requestContext,
                    progressCounter,
                    $"Smooth task {taskId}: {status}",
                    cancellationToken: cancellationToken);
            }

            await requestContext.Server.SendMessageNotificationAsync($"Smooth task {taskId} finished with status={status}.", LoggingLevel.Info, cancellationToken);

            var links = new List<ResourceLinkBlock>();
            var sourceUrls = new List<string>();

            var downloadsUrl = currentTask["downloads_url"]?.GetValue<string>();
            var recordingUrl = currentTask["recording_url"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(downloadsUrl))
                sourceUrls.Add(downloadsUrl);
            if (!string.IsNullOrWhiteSpace(recordingUrl))
                sourceUrls.Add(recordingUrl);

            var baseName = (filename?.ToOutputFileName() ?? requestContext.ToOutputFileName()).ToOutputFileName();
            var uploadIndex = 0;

            foreach (var sourceUrl in sourceUrls.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, sourceUrl, cancellationToken);
                foreach (var file in files)
                {
                    uploadIndex++;
                    var fallbackName = $"{baseName}-{uploadIndex}{GuessExtension(file.Filename, file.MimeType)}";
                    var outputName = string.IsNullOrWhiteSpace(file.Filename)
                        ? fallbackName
                        : file.Filename.ToOutputFileName();

                    var uploaded = await requestContext.Server.Upload(
                        serviceProvider,
                        outputName,
                        BinaryData.FromBytes(file.Contents.ToArray()),
                        cancellationToken);

                    if (uploaded != null)
                        links.Add(uploaded);
                }
            }

            if (links.Count == 0)
            {
                var fallbackJson = currentTask.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                var uploaded = await requestContext.Server.Upload(
                    serviceProvider,
                    $"{baseName}-result.json",
                    BinaryData.FromString(fallbackJson),
                    cancellationToken);

                if (uploaded != null)
                    links.Add(uploaded);
            }

            var structured = new JsonObject
            {
                ["provider"] = "smooth",
                ["operation"] = "submit_task_and_wait",
                ["request"] = payload,
                ["submitResponse"] = submitResponse,
                ["task"] = currentTask,
                ["taskId"] = taskId,
                ["status"] = status,
                ["uploadedResourceCount"] = links.Count,
                ["uploadedFromUrls"] = new JsonArray([.. sourceUrls.Select(a => JsonValue.Create(a))])
            };

            var summary = $"Smooth task {taskId} finished with status={status}. Uploaded resources={links.Count}.";
            var content = new List<ContentBlock> { summary.ToTextContentBlock() };
            content.AddRange(links);

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = (structured).ToJsonElement(),
                Content = [.. content]
            };
        });

    private static JsonObject BuildPayload(
        string? task,
        bool openSessionOnly,
        string? url,
        string? responseModelJson,
        string? metadataJson,
        string? filesJson,
        string agent,
        int maxSteps,
        string device,
        string? allowedUrlsJson,
        bool enableRecording,
        string? profileId,
        bool profileReadOnly,
        bool stealthMode,
        string? proxyServer,
        string? proxyUsername,
        string? proxyPassword,
        bool? useAdblock,
        string? additionalToolsJson,
        string? extensionsJson,
        bool showCursor)
    {
        var payload = new JsonObject
        {
            ["task"] = openSessionOnly ? null : task?.Trim(),
            ["agent"] = agent,
            ["max_steps"] = maxSteps,
            ["device"] = device,
            ["enable_recording"] = enableRecording,
            ["profile_read_only"] = profileReadOnly,
            ["stealth_mode"] = stealthMode,
            ["show_cursor"] = showCursor
        };

        AddOptionalString(payload, "url", url);
        AddOptionalString(payload, "profile_id", profileId);
        AddOptionalString(payload, "proxy_server", proxyServer);
        AddOptionalString(payload, "proxy_username", proxyUsername);
        AddOptionalString(payload, "proxy_password", proxyPassword);

        if (useAdblock.HasValue)
            payload["use_adblock"] = useAdblock.Value;

        AddOptionalJson(payload, "response_model", responseModelJson, expected: JsonValueKind.Object);
        AddOptionalJson(payload, "metadata", metadataJson, expected: JsonValueKind.Object);
        AddOptionalJson(payload, "files", filesJson, expected: JsonValueKind.Array);
        AddOptionalJson(payload, "allowed_urls", allowedUrlsJson, expected: JsonValueKind.Array);
        AddOptionalJson(payload, "additional_tools", additionalToolsJson, expected: JsonValueKind.Object);
        AddOptionalJson(payload, "extensions", extensionsJson, expected: JsonValueKind.Array);

        return payload;
    }

    private static void AddOptionalString(JsonObject payload, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            payload[name] = value.Trim();
    }

    private static void AddOptionalJson(JsonObject payload, string name, string? rawJson, JsonValueKind expected)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            throw new ValidationException($"{name} is not valid JSON: {ex.Message}");
        }

        if (node == null)
            return;

        var kind = node.GetValueKind();
        if (kind != expected)
            throw new ValidationException($"{name} must be a JSON {expected.ToString().ToLowerInvariant()}.");

        payload[name] = node;
    }

    private static long GetLastEventTimestamp(JsonObject task)
    {
        if (task["events"] is not JsonArray events || events.Count == 0)
            return 0;

        return events
            .OfType<JsonObject>()
            .Select(e => e["timestamp"]?.GetValue<long?>())
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool IsRunningStatus(string? status)
        => string.Equals(status, "waiting", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);

    private static string GuessExtension(string? filename, string? mimeType)
    {
        var ext = Path.GetExtension(filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(ext))
            return ext;

        return mimeType?.ToLowerInvariant() switch
        {
            "application/zip" => ".zip",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "application/json" => ".json",
            _ => ".bin"
        };
    }
}

