using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;

namespace MCPhappey.Tools.Runway;

public static class RunwayClientExtensions
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Polls a Runway task until completion and uploads all output URLs to MCP storage.
    /// </summary>
    public static async Task<CallToolResult?> WaitForTaskAndUploadAsync(
        this RunwayClient client,
        string taskId,
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        string fileExtension,
        CancellationToken ct = default)
    {
        string? status = null;
        JsonNode? json;

        do
        {
            using var resp = await client.HttpGetAsync($"v1/tasks/{taskId}", ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"{resp.StatusCode}: {text}");

            json = JsonNode.Parse(text);
            status = json?["status"]?.ToString();

            if (status == "SUCCEEDED")
            {
                var outputs = json?["output"]?.AsArray();
                if (outputs == null || outputs.Count == 0)
                    throw new Exception("No outputs returned by Runway API.");

                var downloadService = sp.GetRequiredService<DownloadService>();
                var uploadedResources = new List<ContentBlock>();

                int index = 0;
                foreach (var output in outputs)
                {
                    var outputUrl = output?.ToString();
                    if (string.IsNullOrWhiteSpace(outputUrl))
                        continue;

                    var allItems = await downloadService.DownloadContentAsync(sp, rc.Server, outputUrl, ct);
                    var bytes = allItems.FirstOrDefault() ?? throw new Exception("File missing");

                    var filename = rc.ToOutputFileName($"{index++}.{fileExtension.TrimStart('.')}");

                    var uploaded = await rc.Server.Upload(
                        sp,
                        filename,
                        bytes.Contents,
                        ct);

                    if (uploaded != null)
                        uploadedResources.Add(uploaded);
                }

                if (uploadedResources.Count == 0)
                    throw new Exception("No valid outputs could be uploaded.");

                return uploadedResources.ToCallToolResult();
            }

            if (status == "FAILED")
            {
                var reason = json?["failure"]?.ToString() ?? "Unknown failure.";
                var code = json?["failureCode"]?.ToString() ?? "";
                throw new Exception($"Runway task failed: {reason} ({code})");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct);

        } while (status != "SUCCEEDED" && status != "FAILED");

        throw new TimeoutException($"Runway task {taskId} did not complete.");
    }

    /// <summary>
    /// Lightweight result when waiting is disabled.
    /// </summary>
    public static CallToolResult CreateImmediateTaskResult(this RunwayClient _, string taskId)
    {
        var obj = new JsonObject
        {
            ["taskId"] = taskId,
            ["status"] = "PENDING"
        };

        return obj.ToJsonContent("https://api.dev.runwayml.com/v1/tasks").ToCallToolResult();
    }

    /// <summary>
    /// Extracts the task ID from a Runway API response JSON.
    /// </summary>
    public static string ExtractTaskId(this RunwayClient _, JsonNode? json)
        => json?["id"]?.ToString() ?? throw new Exception("No task ID returned from Runway API.");

    /// <summary>
    /// Internal GET wrapper using the existing RunwayClient HttpClient.
    /// </summary>
    internal static Task<HttpResponseMessage> HttpGetAsync(this RunwayClient client, string path, CancellationToken ct)
        => client.HttpGetInternalAsync(path, ct);

    private static async Task<HttpResponseMessage> HttpGetInternalAsync(this RunwayClient client, string path, CancellationToken ct)
    {
        var field = typeof(RunwayClient).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(client) is not HttpClient http)
            throw new InvalidOperationException("RunwayClient missing HttpClient instance.");
        return await http.GetAsync(path, ct);
    }
}
