using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookCalendarGroups
{
    [Description("Create a personal Outlook calendar group for the signed-in user. This is not a Microsoft 365 Group.")]
    [McpServerTool(Title = "Create Outlook calendar group", Name = "graph_outlook_calendar_groups_create",
        UseStructuredContent = true, OutputSchemaType = typeof(CalendarGroup), Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Create(
        [Description("Calendar-group name.")] string name,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new CalendarGroupInput { Name = name }, cancellationToken);
            if (input is null || rejected is not null) throw new Exception(JsonSerializer.Serialize(rejected));
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
            return await client.Me.CalendarGroups.PostAsync(
                new CalendarGroup { Name = input.Name.Trim() }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a personal Outlook calendar group. This does not delete Microsoft 365 Groups.")]
    [McpServerTool(Title = "Delete Outlook calendar group", Name = "graph_outlook_calendar_groups_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Delete(
        [Description("Outlook calendar-group ID to delete.")] string calendarGroupId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteCalendarGroupInput>(calendarGroupId,
            async _ => await client.Me.CalendarGroups[calendarGroupId].DeleteAsync(cancellationToken: cancellationToken),
            "Outlook calendar group deleted.", cancellationToken)));

    [Description("Review the new personal Outlook calendar group.")]
    public sealed class CalendarGroupInput
    {
        [Required, JsonPropertyName("name")] public string Name { get; set; } = default!;
    }

    [Description("Please confirm the personal Outlook calendar-group ID to delete: {0}")]
    public sealed class DeleteCalendarGroupInput : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }
}
