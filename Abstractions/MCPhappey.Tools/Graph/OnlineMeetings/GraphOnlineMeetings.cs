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

    [Description("Please review the online meeting fields.")]
    public sealed class CreateOnlineMeetingInput
    {
        [JsonPropertyName("subject"), Required] public string Subject { get; set; } = default!;
        [JsonPropertyName("startDateTime"), Required] public DateTimeOffset StartDateTime { get; set; }
        [JsonPropertyName("endDateTime"), Required] public DateTimeOffset EndDateTime { get; set; }
        [JsonPropertyName("externalId")] public string? ExternalId { get; set; }
    }
}
