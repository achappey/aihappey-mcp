using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Freepik.ImprovePrompt;

public static class FreepikImprovePrompt
{
    [Description("Improve a prompt for AI image or video generation, then poll the task until it completes.")]
    [McpServerTool(
        Title = "Freepik Improve Prompt",
        Name = "freepik_improve_prompt",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> FreepikImprovePrompt_Create(
        [Description("Text prompt to improve for AI generation. Can be empty to generate a creative prompt.")][Required] string prompt,
        [Description("Type of generation: image or video.")][Required] string type,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Language code for the improved prompt (ISO 639-1). Default: en.")] string? language = null,
        [Description("Polling interval in seconds (default 5).")][Range(1, 120)] int pollIntervalSeconds = 5,
        [Description("Maximum polling attempts before timeout (default 60).")][Range(1, 600)] int maxPollAttempts = 60,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var client = serviceProvider.GetRequiredService<FreepikClient>();

        var payload = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["type"] = type
        };

        if (!string.IsNullOrWhiteSpace(language))
            payload["language"] = language;

        var createResult = await client.PostAsync("/v1/ai/improve-prompt", payload, cancellationToken)
            ?? throw new Exception("Freepik returned no response.");

        var taskId = createResult?["data"]?["task_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return new CallToolResult
            {
                StructuredContent = createResult
            };
        }

        JsonNode? lastResult = createResult;
        var attempts = 0;

        while (attempts < maxPollAttempts)
        {
            attempts++;

            var statusResult = await client.GetAsync($"/v1/ai/improve-prompt/{taskId}", cancellationToken)
                ?? throw new Exception("Freepik returned no response while polling.");

            lastResult = statusResult;
            var status = statusResult?["data"]?["status"]?.GetValue<string>()?.ToUpperInvariant();

            if (status is "COMPLETED" or "FAILED")
                break;

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken);
        }

        return new CallToolResult
        {
            StructuredContent = lastResult
        };
    }));
}
