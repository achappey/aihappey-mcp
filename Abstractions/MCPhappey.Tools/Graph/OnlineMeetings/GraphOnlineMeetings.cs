using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OnlineMeetings;

public static class GraphOnlineMeetings
{
    [Description("Create a Microsoft Teams online meeting organized by the signed-in user.")]
    [McpServerTool(Title = "Create online meeting", Destructive = true,
        OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(OnlineMeeting))]
    public static async Task<CallToolResult?> GraphOnlineMeetings_Create(
        [Description("Meeting subject.")] string subject,
        [Description("Meeting start date and time in UTC.")] DateTimeOffset startDateTime,
        [Description("Meeting end date and time in UTC.")] DateTimeOffset endDateTime,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional caller-provided unique external ID.")] string? externalId = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(new CreateOnlineMeetingInput
            {
                Subject = subject, StartDateTime = startDateTime,
                EndDateTime = endDateTime, ExternalId = externalId
            }, cancellationToken);
            if (notAccepted is not null || input is null) return default(OnlineMeeting);
            if (input.EndDateTime <= input.StartDateTime)
                throw new ValidationException("The online meeting end must be later than its start.");

            return await client.Me.OnlineMeetings.PostAsync(new OnlineMeeting
            {
                Subject = input.Subject,
                StartDateTime = input.StartDateTime,
                EndDateTime = input.EndDateTime,
                ExternalId = input.ExternalId
            }, cancellationToken: cancellationToken);
        })));

    [Description("Update an online meeting organized by the signed-in user. Only supplied values are changed.")]
    [McpServerTool(Title = "Update online meeting", Name = "graph_online_meetings_update",
        Destructive = true, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(OnlineMeeting))]
    public static async Task<CallToolResult?> GraphOnlineMeetings_Update(
        [Description("Online meeting ID.")] string meetingId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated meeting subject.")] string? subject = null,
        [Description("Updated meeting start date and time in UTC.")] DateTimeOffset? startDateTime = null,
        [Description("Updated meeting end date and time in UTC.")] DateTimeOffset? endDateTime = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var current = await client.Me.OnlineMeetings[meetingId]
                .GetAsync(cancellationToken: cancellationToken)
                ?? throw new ValidationException("Online meeting was not found.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new UpdateOnlineMeetingInput
                {
                    Subject = subject,
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(OnlineMeeting);
            if (input.Subject is null && input.StartDateTime is null && input.EndDateTime is null)
                throw new ValidationException("At least one online meeting field must be supplied.");

            var effectiveStart = input.StartDateTime ?? current.StartDateTime;
            var effectiveEnd = input.EndDateTime ?? current.EndDateTime;
            if (effectiveStart is not null && effectiveEnd is not null && effectiveEnd <= effectiveStart)
                throw new ValidationException("The online meeting end must be later than its start.");

            return await client.Me.OnlineMeetings[meetingId].PatchAsync(new OnlineMeeting
            {
                Subject = input.Subject,
                StartDateTime = input.StartDateTime,
                EndDateTime = input.EndDateTime
            }, cancellationToken: cancellationToken);
        })));

    [Description("Delete an online meeting organized by the signed-in user.")]
    [McpServerTool(Title = "Delete online meeting", Name = "graph_online_meetings_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOnlineMeetings_Delete(
        [Description("Online meeting ID to delete.")] string meetingId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteOnlineMeetingInput>(
            meetingId,
            async _ => await client.Me.OnlineMeetings[meetingId].DeleteAsync(cancellationToken: cancellationToken),
            "Online meeting deleted.", cancellationToken));

    [Description("Please review the online meeting fields.")]
    public sealed class CreateOnlineMeetingInput
    {
        [JsonPropertyName("subject"), Required] public string Subject { get; set; } = default!;
        [JsonPropertyName("startDateTime"), Required] public DateTimeOffset StartDateTime { get; set; }
        [JsonPropertyName("endDateTime"), Required] public DateTimeOffset EndDateTime { get; set; }
        [JsonPropertyName("externalId")] public string? ExternalId { get; set; }
    }

    [Description("Please review the online meeting fields to update.")]
    public sealed class UpdateOnlineMeetingInput
    {
        [JsonPropertyName("subject")] public string? Subject { get; set; }
        [JsonPropertyName("startDateTime")] public DateTimeOffset? StartDateTime { get; set; }
        [JsonPropertyName("endDateTime")] public DateTimeOffset? EndDateTime { get; set; }
    }

    [Description("Please confirm the online meeting ID to delete: {0}")]
    public sealed class DeleteOnlineMeetingInput : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }
}
