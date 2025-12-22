using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Runway.Tasks;

public static class RunwayTasks
{
    [Description("Check or wait for the completion of an existing Runway task.")]
    [McpServerTool(
        Title = "Check Runway task statuss",
        Name = "runway_task_status",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Runway_CheckTaskStatus(
        [Description("Runway task ID to check (UUID).")]
        [Required]
        string taskId,

        [Description("File extension for downloaded outputs (e.g., 'mp4', 'wav', 'png').")]
        string? fileExtension,

        [Description("Wait until the task completes before returning.")]
        bool? waitUntilCompleted,

        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
        await rc.WithStructuredContent(async () =>
    {
        var runway = sp.GetRequiredService<RunwayClient>();
        fileExtension ??= "bin"; // default generic binary file extension

        if (waitUntilCompleted == true)
        {
            // ⏳ Poll and upload all completed outputs
            return await runway.WaitForTaskAndUploadAsync(taskId, sp, rc, fileExtension, ct);
        }

        // 🔍 Just check the current status and return info
        using var resp = await runway.HttpGetAsync($"v1/tasks/{taskId}", ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        // Return raw JSON status info for the user
        return text.ToJsonContent("https://api.dev.runwayml.com/v1/tasks").ToCallToolResult();
    }));
}
