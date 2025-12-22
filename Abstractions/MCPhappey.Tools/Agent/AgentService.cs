using System.ComponentModel;
using MCPhappey.Common.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Agent;

public static class AgentService
{
    [Description("Use the tool to think about something. It will not obtain new information or change the database, but just append the thought to the log. Use it when complex reasoning or some cache memory is needed.")]
    [McpServerTool(Title = "Think and append to log", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult> Agent_Think(
        [Description("A thought to think about.")] string thought) =>
            await Task.FromResult(thought.ToTextCallToolResponse());

    [Description("Pause execution for the specified duration and log the pause event.")]
    [McpServerTool(Title = "Pause", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult> Agent_Pause(
           [Description("Duration in seconds to pause. Must be >= 0.")] int duration_seconds,
           CancellationToken cancellationToken = default)
    {
        if (duration_seconds < 0)
            duration_seconds = 0;

        await Task.Delay(TimeSpan.FromSeconds(duration_seconds), cancellationToken: cancellationToken);

        return $"Paused for {duration_seconds} seconds".ToTextCallToolResponse();
    }
}

