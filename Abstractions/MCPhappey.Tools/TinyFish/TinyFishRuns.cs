using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.TinyFish;

public static class TinyFishRuns
{
    private const string RunsBatchPath = "v1/runs/batch";
    private const string RunsBatchCancelPath = "v1/runs/batch/cancel";

    [Description("Get multiple TinyFish runs by run IDs. Returns found runs and not-found IDs.")]
    [McpServerTool(
        Title = "TinyFish get runs by IDs",
        Name = "tinyfish_runs_batch_get",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> TinyFish_Runs_Batch_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Array of TinyFish run IDs to fetch (1-100).")]
        string[] run_ids,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
        {
            ValidateRunIds(run_ids);

            var request = new JsonObject
            {
                ["run_ids"] = new JsonArray(run_ids.Select(id => JsonValue.Create(id)!).ToArray())
            };

            var response = await SendPostJsonAsync(serviceProvider, RunsBatchPath, request, cancellationToken);

            var structured = new JsonObject
            {
                ["provider"] = "tinyfish",
                ["endpoint"] = RunsBatchPath,
                ["request"] = request,
                ["data"] = response["data"]?.DeepClone(),
                ["not_found"] = response["not_found"]?.DeepClone(),
                ["response"] = response.DeepClone()
            };

            return structured;
        }));

    [Description("Cancel multiple TinyFish runs by run IDs. Returns cancelled runs and not-found IDs.")]
    [McpServerTool(
        Title = "TinyFish cancel runs by IDs",
        Name = "tinyfish_runs_batch_cancel",
        Destructive = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> TinyFish_Runs_Batch_Cancel(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Array of TinyFish run IDs to cancel (1-100).")]
        string[] run_ids,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithStructuredContent(async () =>
        {
            ValidateRunIds(run_ids);

            var request = new JsonObject
            {
                ["run_ids"] = new JsonArray(run_ids.Select(id => JsonValue.Create(id)!).ToArray())
            };

            var response = await SendPostJsonAsync(serviceProvider, RunsBatchCancelPath, request, cancellationToken);

            var structured = new JsonObject
            {
                ["provider"] = "tinyfish",
                ["endpoint"] = RunsBatchCancelPath,
                ["request"] = request,
                ["data"] = response["data"]?.DeepClone(),
                ["not_found"] = response["not_found"]?.DeepClone(),
                ["response"] = response.DeepClone()
            };

            return structured;
        }));

    [Description("Cancel a TinyFish run by run ID.")]
    [McpServerTool(
        Title = "TinyFish cancel run by ID",
        Name = "tinyfish_runs_cancel_by_id",
        Destructive = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> TinyFish_Runs_Cancel_By_Id(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("TinyFish run ID to cancel.")] string id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var runId = id.Trim();

            var endpoint = $"v1/runs/{runId}/cancel";
            var request = new JsonObject
            {
                ["id"] = runId
            };

            var response = await SendPostJsonAsync(serviceProvider, endpoint, body: null, cancellationToken);
            var status = response["status"]?.GetValue<string>()
                ?? response["data"]?["status"]?.GetValue<string>()
                ?? "UNKNOWN";

            var structured = new JsonObject
            {
                ["provider"] = "tinyfish",
                ["endpoint"] = endpoint,
                ["request"] = request,
                ["runId"] = runId,
                ["status"] = status,
                ["response"] = response.DeepClone()
            };

            return structured;
        }));

    private static void ValidateRunIds(string[] runIds)
    {
        ArgumentNullException.ThrowIfNull(runIds);
        if (runIds.Length is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(runIds), "run_ids must contain between 1 and 100 IDs.");

        if (runIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("run_ids must not contain empty values.", nameof(runIds));
    }

    private static async Task<JsonObject> SendPostJsonAsync(
        IServiceProvider serviceProvider,
        string endpoint,
        JsonObject? body,
        CancellationToken cancellationToken)
    {
        using var client = serviceProvider.CreateTinyFishClient("application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);

        if (body is not null)
        {
            req.Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json");
        }

        using var resp = await client.SendAsync(req, cancellationToken);
        var responseText = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"TinyFish request failed ({(int)resp.StatusCode}) on {endpoint}: {responseText}");

        if (string.IsNullOrWhiteSpace(responseText))
            return new JsonObject();

        return JsonNode.Parse(responseText) as JsonObject
               ?? new JsonObject { ["raw"] = responseText };
    }
}

