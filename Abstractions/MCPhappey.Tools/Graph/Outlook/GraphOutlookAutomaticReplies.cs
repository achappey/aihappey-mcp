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

public static class GraphOutlookAutomaticReplies
{
    [Description("Configure automatic replies for the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Set Outlook automatic replies", Name = "graph_outlook_automatic_replies_set",
        UseStructuredContent = true, OutputSchemaType = typeof(MailboxSettings),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookAutomaticReplies_Set(
        [Description("Automatic reply mode: AlwaysEnabled or Scheduled.")] AutomaticRepliesStatus status,
        [Description("Internal automatic reply message, in HTML or plain text.")] string internalReplyMessage,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("External automatic reply message, in HTML or plain text.")] string? externalReplyMessage = null,
        [Description("Who receives external replies: None, ContactsOnly, or All.")] ExternalAudienceScope externalAudience = ExternalAudienceScope.None,
        [Description("Schedule start date/time. Required when status is Scheduled.")] DateTimeOffset? scheduledStartDateTime = null,
        [Description("Schedule end date/time. Required when status is Scheduled.")] DateTimeOffset? scheduledEndDateTime = null,
        [Description("Windows time-zone name used for the schedule, for example 'W. Europe Standard Time'.")] string timeZone = "UTC",
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (status == AutomaticRepliesStatus.Disabled)
                throw new ValidationException("Use graph_outlook_automatic_replies_disable to disable automatic replies.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new AutomaticRepliesInput
                {
                    Status = status,
                    InternalReplyMessage = internalReplyMessage,
                    ExternalReplyMessage = externalReplyMessage,
                    ExternalAudience = externalAudience,
                    ScheduledStartDateTime = scheduledStartDateTime,
                    ScheduledEndDateTime = scheduledEndDateTime,
                    TimeZone = timeZone
                }, cancellationToken);

            if (notAccepted is not null || input is null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));
            Validate(input);

            var settings = new AutomaticRepliesSetting
            {
                Status = input.Status,
                InternalReplyMessage = input.InternalReplyMessage.Trim(),
                ExternalReplyMessage = input.ExternalReplyMessage,
                ExternalAudience = input.ExternalAudience,
                ScheduledStartDateTime = input.Status == AutomaticRepliesStatus.Scheduled
                    ? ToGraphDateTime(input.ScheduledStartDateTime!.Value, input.TimeZone)
                    : null,
                ScheduledEndDateTime = input.Status == AutomaticRepliesStatus.Scheduled
                    ? ToGraphDateTime(input.ScheduledEndDateTime!.Value, input.TimeZone)
                    : null
            };

            return await client.Me.MailboxSettings.PatchAsync(
                new MailboxSettings { AutomaticRepliesSetting = settings },
                cancellationToken: cancellationToken);
        })));

    [Description("Disable automatic replies for the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Disable Outlook automatic replies", Name = "graph_outlook_automatic_replies_disable",
        UseStructuredContent = true, OutputSchemaType = typeof(MailboxSettings),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookAutomaticReplies_Disable(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new DisableAutomaticRepliesInput(), cancellationToken);
            if (notAccepted is not null || input is null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            return await client.Me.MailboxSettings.PatchAsync(
                new MailboxSettings
                {
                    AutomaticRepliesSetting = new AutomaticRepliesSetting
                    {
                        Status = AutomaticRepliesStatus.Disabled
                    }
                }, cancellationToken: cancellationToken);
        })));

    private static void Validate(AutomaticRepliesInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.InternalReplyMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TimeZone);
        if (input.Status != AutomaticRepliesStatus.Scheduled) return;
        if (input.ScheduledStartDateTime is null || input.ScheduledEndDateTime is null)
            throw new ValidationException("Scheduled automatic replies require start and end date/times.");
        if (input.ScheduledEndDateTime <= input.ScheduledStartDateTime)
            throw new ValidationException("The scheduled end must be after the scheduled start.");
    }

    private static DateTimeTimeZone ToGraphDateTime(DateTimeOffset value, string timeZone) => new()
    {
        DateTime = value.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
        TimeZone = timeZone.Trim()
    };

    [Description("Review the Outlook automatic reply settings.")]
    public sealed class AutomaticRepliesInput
    {
        [Required]
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AutomaticRepliesStatus? Status { get; set; }

        [Required]
        [JsonPropertyName("internalReplyMessage")]
        public string InternalReplyMessage { get; set; } = default!;

        [JsonPropertyName("externalReplyMessage")]
        public string? ExternalReplyMessage { get; set; }

        [JsonPropertyName("externalAudience")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExternalAudienceScope? ExternalAudience { get; set; }

        [JsonPropertyName("scheduledStartDateTime")]
        public DateTimeOffset? ScheduledStartDateTime { get; set; }

        [JsonPropertyName("scheduledEndDateTime")]
        public DateTimeOffset? ScheduledEndDateTime { get; set; }

        [Required]
        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; } = "UTC";
    }

    [Description("Confirm that Outlook automatic replies should be disabled.")]
    public sealed class DisableAutomaticRepliesInput
    {
        [JsonPropertyName("disable")]
        public bool Disable { get; set; } = true;
    }
}
