using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.TinyFish;

public static class TinyFishAutomation
{
    private const string RunSsePath = "v1/automation/run-sse";

    [Description("Run TinyFish browser automation with SSE streaming and forward updates via MCP notifications.")]
    [McpServerTool(
        Title = "TinyFish automation run (SSE)",
        Name = "tinyfish_automation_run",
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> TinyFish_Automation_Run(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target website URL to automate.")] string url,
        [Description("Natural language goal to accomplish on the website.")] string goal,
        [Description("Optional browser profile: lite or stealth.")] string? browser_profile = null,
        [Description("Optional proxy enable flag. When set, proxy_config is sent.")] bool? proxy_enabled = null,
        [Description("Optional proxy country code: US, GB, CA, DE, FR, JP, AU.")] string? proxy_country_code = null,
        [Description("Optional integration name for analytics, for example dify, zapier, n8n.")] string? api_integration = null,
        [Description("Optional feature flag to enable agent memory.")] bool? feature_enable_agent_memory = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            ArgumentException.ThrowIfNullOrWhiteSpace(goal);

            var payload = BuildPayload(
                url,
                goal,
                browser_profile,
                proxy_enabled,
                proxy_country_code,
                api_integration,
                feature_enable_agent_memory);

            using var client = serviceProvider.CreateTinyFishClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, RunSsePath)
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"TinyFish run-sse failed ({(int)resp.StatusCode}): {errorBody}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var events = new JsonArray();
            string? runId = null;
            string? streamingUrl = null;
            JsonObject? completeEvent = null;
            int? progressCounter = 0;
            var dataBuffer = new StringBuilder();

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(line))
                {
                    progressCounter = await FlushSseData(
                        dataBuffer,
                        events,
                        requestContext,
                        progressCounter,
                        runIdRef: v => runId = v,
                        streamingUrlRef: v => streamingUrl = v,
                        completeRef: v => completeEvent = v,
                        cancellationToken);
                    continue;
                }

                if (line.StartsWith(':'))
                    continue;

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var data = line.Length >= 5 ? line[5..].TrimStart() : string.Empty;
                    dataBuffer.AppendLine(data);
                }
            }

            progressCounter = await FlushSseData(
                dataBuffer,
                events,
                requestContext,
                progressCounter,
                runIdRef: v => runId = v,
                streamingUrlRef: v => streamingUrl = v,
                completeRef: v => completeEvent = v,
                cancellationToken);

            var structured = new JsonObject
            {
                ["provider"] = "tinyfish",
                ["endpoint"] = RunSsePath,
                ["request"] = payload,
                ["runId"] = runId,
                ["streamingUrl"] = streamingUrl,
                ["events"] = events,
                ["eventCount"] = events.Count,
                ["complete"] = completeEvent,
                ["status"] = completeEvent?["status"]?.GetValue<string>(),
                ["resultJson"] = completeEvent?["resultJson"]
            };

            var status = completeEvent?["status"]?.GetValue<string>() ?? "UNKNOWN";
            var summary = $"TinyFish automation stream finished. Status={status}, RunId={runId ?? "n/a"}, Events={events.Count}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = structured,
                Content = [summary.ToTextContentBlock()]
            };
        });

    private static JsonObject BuildPayload(
        string url,
        string goal,
        string? browserProfile,
        bool? proxyEnabled,
        string? proxyCountryCode,
        string? apiIntegration,
        bool? featureEnableAgentMemory)
    {
        var payload = new JsonObject
        {
            ["url"] = url,
            ["goal"] = goal
        };

        if (!string.IsNullOrWhiteSpace(browserProfile))
            payload["browser_profile"] = browserProfile.Trim().ToLowerInvariant();

        if (proxyEnabled.HasValue)
        {
            payload["proxy_config"] = new JsonObject
            {
                ["enabled"] = proxyEnabled.Value,
                ["country_code"] = string.IsNullOrWhiteSpace(proxyCountryCode)
                    ? null
                    : proxyCountryCode.Trim().ToUpperInvariant()
            };
        }

        if (!string.IsNullOrWhiteSpace(apiIntegration))
            payload["api_integration"] = apiIntegration.Trim();

        if (featureEnableAgentMemory.HasValue)
        {
            payload["feature_flags"] = new JsonObject
            {
                ["enable_agent_memory"] = featureEnableAgentMemory.Value
            };
        }

        return payload;
    }

    private static async Task<int?> FlushSseData(
        StringBuilder dataBuffer,
        JsonArray events,
        RequestContext<CallToolRequestParams> requestContext,
        int? progressCounter,
        Action<string?> runIdRef,
        Action<string?> streamingUrlRef,
        Action<JsonObject?> completeRef,
        CancellationToken cancellationToken)
    {
        var data = dataBuffer.ToString().Trim();
        dataBuffer.Clear();

        if (string.IsNullOrWhiteSpace(data))
            return progressCounter;

        JsonNode parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(data) ?? JsonValue.Create(data)!;
        }
        catch
        {
            parsedNode = JsonValue.Create(data)!;
        }

        events.Add(parsedNode.DeepClone());

        if (parsedNode is not JsonObject parsedObject)
        {
            await requestContext.Server.SendMessageNotificationAsync($"TinyFish SSE raw event: {data}", LoggingLevel.Info, cancellationToken);
            progressCounter = await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter,
                "TinyFish SSE event received",
                cancellationToken: cancellationToken);
            return progressCounter;
        }

        var type = parsedObject["type"]?.GetValue<string>()?.Trim().ToUpperInvariant() ?? "UNKNOWN";
        var runId = parsedObject["runId"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(runId))
            runIdRef(runId);

        string message;
        LoggingLevel level;

        switch (type)
        {
            case "STARTED":
                message = $"TinyFish STARTED (runId={runId ?? "n/a"}).";
                level = LoggingLevel.Info;
                break;

            case "STREAMING_URL":
                var streamUrl = parsedObject["streamingUrl"]?.GetValue<string>();
                streamingUrlRef(streamUrl);
                message = $"TinyFish STREAMING_URL (runId={runId ?? "n/a"}): {streamUrl}";
                level = LoggingLevel.Info;
                break;

            case "PROGRESS":
                var purpose = parsedObject["purpose"]?.GetValue<string>() ?? "Progress update";
                message = $"TinyFish PROGRESS (runId={runId ?? "n/a"}): {purpose}";
                level = LoggingLevel.Info;
                break;

            case "HEARTBEAT":
                message = "TinyFish HEARTBEAT.";
                level = LoggingLevel.Debug;
                break;

            case "COMPLETE":
                completeRef(parsedObject);
                var status = parsedObject["status"]?.GetValue<string>() ?? "UNKNOWN";
                var error = parsedObject["error"]?.GetValue<string>();
                message = string.IsNullOrWhiteSpace(error)
                    ? $"TinyFish COMPLETE (runId={runId ?? "n/a"}) status={status}."
                    : $"TinyFish COMPLETE (runId={runId ?? "n/a"}) status={status}. Error={error}";
                level = status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                    ? LoggingLevel.Info
                    : LoggingLevel.Warning;
                break;

            default:
                message = $"TinyFish {type} (runId={runId ?? "n/a"}).";
                level = LoggingLevel.Info;
                break;
        }

        await requestContext.Server.SendMessageNotificationAsync(message, level, cancellationToken);
        progressCounter = await requestContext.Server.SendProgressNotificationAsync(
            requestContext,
            progressCounter,
            message,
            cancellationToken: cancellationToken);

        return progressCounter;
    }
}

