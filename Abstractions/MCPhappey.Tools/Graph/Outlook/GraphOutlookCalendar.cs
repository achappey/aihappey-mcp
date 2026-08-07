using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookCalendar
{
    [Description("Create a secondary calendar in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Create Outlook calendar",
        Name = "graph_outlook_calendar_create_calendar",
        UseStructuredContent = true,
        OutputSchemaType = typeof(Calendar),
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_CreateCalendar(
        [Description("Name of the new calendar.")] string name,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional hexadecimal calendar color such as #0078D4. Outlook may normalize this value.")] string? hexColor = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphCalendarInput { Name = name, HexColor = hexColor }, cancellationToken);

            if (notAccepted is not null)
                return default(Calendar);

            ValidateCalendarInput(typed);
            return await client.Me.Calendars.PostAsync(
                new Calendar { Name = typed!.Name.Trim(), HexColor = NormalizeHexColor(typed.HexColor) },
                cancellationToken: cancellationToken);
        })));

    [Description("Update a secondary calendar in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Update Outlook calendar",
        Name = "graph_outlook_calendar_update_calendar",
        UseStructuredContent = true,
        OutputSchemaType = typeof(Calendar),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_UpdateCalendar(
        [Description("ID of the calendar to update.")] string calendarId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New calendar name. Leave empty to keep unchanged.")] string? name = null,
        [Description("New hexadecimal calendar color such as #0078D4. Leave empty to keep unchanged.")] string? hexColor = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (name is null && hexColor is null)
                throw new ValidationException("A calendar name or hexadecimal color must be provided.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphCalendarUpdate { Name = name, HexColor = hexColor }, cancellationToken);

            if (notAccepted is not null)
                return default(Calendar);
            if (typed?.Name is not null)
                ArgumentException.ThrowIfNullOrWhiteSpace(typed.Name);

            return await client.Me.Calendars[calendarId].PatchAsync(
                new Calendar
                {
                    Name = typed?.Name?.Trim(),
                    HexColor = typed?.HexColor is null ? null : NormalizeHexColor(typed.HexColor)
                }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a secondary calendar from the signed-in user's Outlook mailbox. The default calendar cannot be deleted.")]
    [McpServerTool(Title = "Delete Outlook calendar",
        Name = "graph_outlook_calendar_delete_calendar",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_DeleteCalendar(
        [Description("ID of the secondary calendar to delete.")] string calendarId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteCalendar>(
            calendarId,
            async _ => await client.Me.Calendars[calendarId].DeleteAsync(cancellationToken: cancellationToken),
            "Outlook calendar deleted.", cancellationToken)));

    [Description("Create a new calendar event in the user's Outlook calendar.")]
    [McpServerTool(Title = "Create Outlook calendar event",
        UseStructuredContent = true,
        OutputSchemaType = typeof(Event),
        Destructive = true)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_CreateCalendarEvent(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Title or subject of the event.")] string? subject = null,
        [Description("Description or body of the event.")] string? body = null,
        [Description("Type of the body content (html or text).")] BodyType? bodyType = null,
        [Description("Start date and time (yyyy-MM-ddTHH:mm:ss format).")] string? startDateTime = null,
        [Description("End date and time (yyyy-MM-ddTHH:mm:ss format).")] string? endDateTime = null,
        [Description("Time zone for the event.")] string? timeZone = null,
        [Description("Location or meeting room.")] string? location = null,
        [Description("E-mail addresses of attendees (comma separated).")] string? attendees = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphCreateCalendarEvent
            {
                Subject = subject ?? string.Empty,
                Body = body,
                BodyType = bodyType,
                StartDateTime = startDateTime ?? string.Empty,
                EndDateTime = endDateTime ?? string.Empty,
                TimeZone = timeZone,
                Location = location,
                Attendees = attendees
            },
            cancellationToken
        );

        var newEvent = new Event
        {
            Subject = typed.Subject,
            Body = new ItemBody
            {
                ContentType = typed.BodyType ?? BodyType.Text,
                Content = typed.Body
            },
            Start = new DateTimeTimeZone
            {
                DateTime = typed.StartDateTime,
                TimeZone = typed.TimeZone ?? "UTC"
            },
            End = new DateTimeTimeZone
            {
                DateTime = typed.EndDateTime,
                TimeZone = typed.TimeZone ?? "UTC"
            },
            Location = new Location
            {
                DisplayName = typed.Location
            },
            Attendees = string.IsNullOrWhiteSpace(typed.Attendees) ? null :
                [.. typed.Attendees.Split(',')
                    .Select(a => new Attendee
                    {
                        EmailAddress = a.ToEmailAddress(),
                        Type = AttendeeType.Required
                    })]
        };

        return await client.Me.Events.PostAsync(newEvent, cancellationToken: cancellationToken);
    }));

    [Description("Update an existing event in the signed-in user's Outlook calendar.")]
    [McpServerTool(Title = "Update Outlook calendar event",
        Name = "graph_outlook_calendar_update_event",
        UseStructuredContent = true,
        OutputSchemaType = typeof(Event),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_UpdateCalendarEvent(
        [Description("The calendar event id.")] string eventId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated subject. Leave empty to keep the current value.")] string? subject = null,
        [Description("Updated body. Leave empty to keep the current value.")] string? body = null,
        [Description("Updated body content type (html or text). Leave empty to keep the current value.")] BodyType? bodyType = null,
        [Description("Updated start date and time in yyyy-MM-ddTHH:mm:ss format. Leave empty to keep the current value.")] string? startDateTime = null,
        [Description("Updated end date and time in yyyy-MM-ddTHH:mm:ss format. Leave empty to keep the current value.")] string? endDateTime = null,
        [Description("Updated Windows time zone. Leave empty to keep the current value.")] string? timeZone = null,
        [Description("Updated location. Leave empty to keep the current value.")] string? location = null,
        [Description("Updated attendee email addresses as comma-separated values. Leave empty to keep the current attendees.")] string? attendees = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateCalendarEvent
                {
                    Subject = subject,
                    Body = body,
                    BodyType = bodyType,
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime,
                    TimeZone = timeZone,
                    Location = location,
                    Attendees = attendees
                }, cancellationToken);

            if (notAccepted is not null)
                return default(Event);

            var update = new Event
            {
                Subject = typed?.Subject,
                Body = typed?.Body is not null || typed?.BodyType is not null
                    ? new ItemBody { Content = typed?.Body, ContentType = typed?.BodyType }
                    : null,
                Start = typed?.StartDateTime is not null
                    ? new DateTimeTimeZone { DateTime = typed.StartDateTime, TimeZone = typed.TimeZone ?? "UTC" }
                    : null,
                End = typed?.EndDateTime is not null
                    ? new DateTimeTimeZone { DateTime = typed.EndDateTime, TimeZone = typed.TimeZone ?? "UTC" }
                    : null,
                Location = typed?.Location is not null ? new Location { DisplayName = typed.Location } : null,
                Attendees = typed?.Attendees is not null
                    ? [.. typed.Attendees.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(address => new Attendee { EmailAddress = address.ToEmailAddress(), Type = AttendeeType.Required })]
                    : null
            };

            return await client.Me.Events[eventId].PatchAsync(update, cancellationToken: cancellationToken);
        })));

    [Description("Delete an event from the signed-in user's Outlook calendar.")]
    [McpServerTool(Title = "Delete Outlook calendar event",
        Name = "graph_outlook_calendar_delete_event",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_DeleteCalendarEvent(
        [Description("The calendar event id to delete.")] string eventId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteCalendarEvent>(
            eventId,
            async _ => await client.Me.Events[eventId].DeleteAsync(cancellationToken: cancellationToken),
            "Calendar event deleted.",
            cancellationToken));

    /// <summary>
    /// Data for creating a calendar event.
    /// </summary>
    [Description("Fill in the details for the new calendar event.")]
    public class GraphCreateCalendarEvent
    {
        [JsonPropertyName("subject")]
        [Required]
        [Description("Title or subject of the event.")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        [Description("Description or body of the event.")]
        public string? Body { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Type of the body content (html or text).")]
        public BodyType? BodyType { get; set; }

        [JsonPropertyName("startDateTime")]
        [Required]
        [Description("Start date and time of the event (yyyy-MM-ddTHH:mm:ss format, e.g., 2025-07-05T13:30:00).")]
        public string StartDateTime { get; set; } = string.Empty;

        [JsonPropertyName("endDateTime")]
        [Required]
        [Description("End date and time of the event (yyyy-MM-ddTHH:mm:ss format, e.g., 2025-07-05T14:30:00).")]
        public string EndDateTime { get; set; } = string.Empty;

        [JsonPropertyName("timeZone")]
        [Description("Time zone for the event (e.g., 'W. Europe Standard Time', 'UTC'). Defaults to UTC.")]
        public string? TimeZone { get; set; }

        [JsonPropertyName("location")]
        [Description("Location or meeting room for the event.")]
        public string? Location { get; set; }

        [JsonPropertyName("attendees")]
        [Description("E-mail addresses of attendees. Use a comma separated list for multiple recipients.")]
        public string? Attendees { get; set; }
    }

    [Description("Please fill in the calendar event fields to update.")]
    public class GraphUpdateCalendarEvent
    {
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BodyType? BodyType { get; set; }

        [JsonPropertyName("startDateTime")]
        public string? StartDateTime { get; set; }

        [JsonPropertyName("endDateTime")]
        public string? EndDateTime { get; set; }

        [JsonPropertyName("timeZone")]
        public string? TimeZone { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("attendees")]
        public string? Attendees { get; set; }
    }

    [Description("Please confirm the calendar event id to delete: {0}")]
    public class GraphDeleteCalendarEvent : MCPhappey.Common.Models.IHasName
    {
        [Required]
        [Description("The calendar event id.")]
        public string Name { get; set; } = default!;
    }

    [Description("Please fill in the Outlook calendar details.")]
    public sealed class GraphCalendarInput
    {
        [Required]
        [JsonPropertyName("name")]
        [Description("Calendar name.")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("hexColor")]
        [Description("Optional hexadecimal calendar color such as #0078D4.")]
        public string? HexColor { get; set; }
    }

    [Description("Please fill in the Outlook calendar fields to update.")]
    public sealed class GraphCalendarUpdate
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("hexColor")]
        public string? HexColor { get; set; }
    }

    [Description("Please confirm the Outlook calendar id to delete: {0}")]
    public sealed class GraphDeleteCalendar : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    private static void ValidateCalendarInput(GraphCalendarInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        _ = NormalizeHexColor(input.HexColor);
    }

    private static string? NormalizeHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.Length != 7 || normalized[0] != '#' ||
            !normalized[1..].All(Uri.IsHexDigit))
            throw new ValidationException("Calendar color must use #RRGGBB hexadecimal format.");

        return normalized.ToUpperInvariant();
    }

}
