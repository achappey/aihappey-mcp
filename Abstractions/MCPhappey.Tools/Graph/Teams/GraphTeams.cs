using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Teams;

public static partial class GraphTeams
{
    [Description("Create a new Microsoft Teams.")]
    [McpServerTool(Title = "Create Microsoft Teams",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_CreateTeam(
        [Description("Displayname of the new channel")]
        string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        TeamVisibilityType? teamVisibilityType = TeamVisibilityType.Private,
        [Description("Description of the new channel")]
        string? description = null,
        CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                new GraphNewTeam
                {
                    DisplayName = displayName,
                    Description = description,
                    Visibility = teamVisibilityType ?? TeamVisibilityType.Private
                },
                cancellationToken
            );
                if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

                var newTeam = new Team
                {
                    Visibility = typed?.Visibility,
                    DisplayName = typed?.DisplayName,
                    Description = typed?.Description,
                    AdditionalData = new Dictionary<string, object>
            {
                {
                    "template@odata.bind" , "https://graph.microsoft.com/beta/teamsTemplates('standard')"
                },
            },
                };

                var teamItem = await client.Teams.PostAsync(newTeam, cancellationToken: cancellationToken);

                return new
                {
                    teamItem?.Id
                };
            })));

    [Description("Edit an existing Microsoft Team.")]
    [McpServerTool(
        Title = "Edit Microsoft Team",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_EditTeam(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("ID of the Team to edit.")]
            string teamId,
        [Description("New display name of the Team. Leave empty to keep the current display name.")]
            string? displayName = null,
        [Description("New description of the Team. Leave null to keep the current description.")]
            string? description = null,
        [Description("New visibility of the Team. Leave null to keep the current visibility.")]
            TeamVisibilityType? visibility = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                new GraphEditTeam
                {
                    TeamId = teamId,
                    DisplayName = displayName,
                    Description = description,
                    Visibility = visibility
                },
                cancellationToken
            );

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            if (typed is null)
                throw new InvalidOperationException("No Team update data was provided.");

            if (string.IsNullOrWhiteSpace(typed.TeamId))
                throw new ValidationException("Team ID is required.");

            if (typed.DisplayName is null
                && typed.Description is null
                && typed.Visibility is null)
            {
                throw new ValidationException(
                    "At least one property must be provided: displayName, description, or visibility.");
            }

            if (typed.DisplayName is not null
                && string.IsNullOrWhiteSpace(typed.DisplayName))
            {
                throw new ValidationException(
                    "Display name cannot be empty or whitespace.");
            }

            var teamUpdate = new Team
            {
                DisplayName = typed.DisplayName,
                Description = typed.Description,
                Visibility = typed.Visibility
            };

            await client.Teams[typed.TeamId].PatchAsync(
                teamUpdate,
                cancellationToken: cancellationToken
            );

            return new
            {
                teamId
            };
        })));

    [Description("Create a new calendar event in the Teams Group calendar.")]
    [McpServerTool(Title = "Create Teams Group calendar event",
        Destructive = true)]
    public static async Task<CallToolResult?> GraphTeams_CreateCalendarEvent(
      RequestContext<CallToolRequestParams> requestContext,
      [Description("ID of the Team.")] string teamId,
      [Description("Title or subject of the event.")] string? subject = null,
      [Description("Description or body of the event.")] string? body = null,
      [Description("Type of the body content (html or text).")] BodyType? bodyType = null,
      [Description("Start date and time (yyyy-MM-ddTHH:mm:ss format).")] string? startDateTime = null,
      [Description("End date and time (yyyy-MM-ddTHH:mm:ss format).")] string? endDateTime = null,
      [Description("Time zone for the event.")] string? timeZone = null,
      [Description("Location or meeting room.")] string? location = null,
      [Description("E-mail addresses of attendees (comma separated).")] string? attendees = null,
      CancellationToken cancellationToken = default) =>
      await ModelContextToolExtensions.WithExceptionCheck(async () =>
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

      return await client.Groups[teamId].Events.PostAsync(newEvent, cancellationToken: cancellationToken);
  })));



}