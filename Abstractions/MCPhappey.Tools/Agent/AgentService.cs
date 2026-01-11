using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
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

    [Description("Ask the MCP client a follow-up question and present a set of options to choose from. This tool uses MCP Elicitation and returns the raw elicitation result (action/content/etc.) as structured output.")]
    [McpServerTool(
        Title = "Ask follow-up question",
        Name = "agent_followup_question",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        Destructive = false)]
    public static async Task<CallToolResult?> Agent_FollowUpQuestion(
        [Description("The question to show to the user/client.")]
        string question,
        [Description("List of options the user can pick from. Each option is shown as a selectable choice.")]
        string[] options,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(question))
                    throw new ArgumentException("Question is required.", nameof(question));

                // Sanitize options (trim, remove empties, distinct)
                var cleaned = (options ?? [])
                    .Select(o => o?.Trim())
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.Ordinal)
                    .OfType<string>()
                    .ToArray();

                if (cleaned.Length == 0)
                    throw new ArgumentException("At least one option is required.", nameof(options));

                const string fieldName = "selection";

                var schema = new ElicitRequestParams.TitledSingleSelectEnumSchema
                {
                    Title = "Selection",
                    OneOf = [.. cleaned
                        .Select(o => new ElicitRequestParams.EnumSchemaOption
                        {
                            Title = o,
                            Const = o
                        })]
                };

                var elicitRequest = new ElicitRequestParams
                {
                    Message = question,
                    RequestedSchema = new ElicitRequestParams.RequestSchema
                    {
                        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                        {
                            [fieldName] = schema
                        },
                        Required = [fieldName]
                    }
                };

                // Returns ElicitResult; wrapper will serialize it as structured tool output.
                return await requestContext.Server.ElicitAsync(elicitRequest, cancellationToken: cancellationToken);
            }));

    [Description("Signal that the agent task is finished. This is a lightweight tool that returns a structured completion payload.")]
    [McpServerTool(
        Title = "Task finished",
        Name = "agent_task_finished",
        ReadOnly = true,
        Idempotent = true,
        OpenWorld = false,
        Destructive = false)]
    public static Task<CallToolResult> Agent_TaskFinished(
        [Description("Required completion comment to send back to the client.")]
        string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Task.FromResult("comment is required".ToErrorCallToolResponse());

        return Task.FromResult(comment.ToTextCallToolResponse());
    }
}

