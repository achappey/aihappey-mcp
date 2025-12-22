using System.ComponentModel;
using System.Xml;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Me.Presence.SetPresence;
using Microsoft.Graph.Beta.Me.Presence.SetStatusMessage;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Teams;

public static partial class GraphTeams
{
    [Description("Set a Teams status message for a user.")]
    [McpServerTool(Title = "Set Teams status message", Destructive = true)]
    public static async Task<CallToolResult?> GraphTeams_SetStatusMessage(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Status message")] string statusMessage,
        [Description("Message type")] BodyType? messageType = BodyType.Text,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
               new GraphSetStatusMessage
               {
                   Message = statusMessage,
                   BodyType = messageType ?? BodyType.Text
               },
               cancellationToken
           );

        SetStatusMessagePostRequestBody body = new()
        {
            StatusMessage = new()
            {
                Message = new()
                {
                    Content = typed?.Message,
                    ContentType = typed?.BodyType,
                },
                ExpiryDateTime = !string.IsNullOrWhiteSpace(typed?.ExpiryDateTime)
                    ? new()
                    {
                        DateTime = typed.ExpiryDateTime,
                        TimeZone = typed.TimeZone ?? "UTC"
                    }
                    : null,
            }
        };

        await graphClient.Me.Presence.SetStatusMessage.PostAsync(body, cancellationToken: cancellationToken);

        return body.ToJsonContentBlock("https://graph.microsoft.com/beta/me/presence")
          .ToCallToolResult();
    }));

    [Description(@"Provide the presence status to set for Teams.
        Supported combinations:
        - Available + Available: Sets presence to Available.
        - Busy + InACall: Sets presence to Busy, InACall.
        - Busy + InAConferenceCall: Sets presence to Busy, InAConferenceCall.
        - Away + Away: Sets presence to Away.
        - DoNotDisturb + Presenting: Sets presence to DoNotDisturb, Presenting.
        ")]
    [McpServerTool(Title = "Set Teams presence", Destructive = true)]
    public static async Task<CallToolResult?> GraphTeams_SetPresence(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Availability (Available, Busy, DoNotDisturb, Away, etc.)")] string availability,
        [Description("Activity (Available, InACall, InAConferenceCall, Presenting, Away, etc.)")] string activity,
        [Description("Expiration duration in ISO8601 duration format, e.g. 'PT1H' for 1 hour. Optional.")] string? expirationDuration = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var oauth = serviceProvider.GetService<OAuthSettings>();

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
               new GraphSetPresence
               {
                   Availability = availability,
                   Activity = activity,
                   ExpirationDuration = expirationDuration
               },
               cancellationToken
           );

        var setPresenceBody = new SetPresencePostRequestBody
        {
            SessionId = oauth?.ClientId,
            Availability = typed?.Availability,
            Activity = typed?.Activity,
            ExpirationDuration = string.IsNullOrEmpty(typed?.ExpirationDuration) ?
                 TimeSpan.FromHours(1) : XmlConvert.ToTimeSpan(typed.ExpirationDuration)
        };

        await graphClient.Me.Presence.SetPresence.PostAsync(setPresenceBody, cancellationToken: cancellationToken);

        return setPresenceBody.ToJsonContentBlock("https://graph.microsoft.com/beta/me/presence")
          .ToCallToolResult();
    }));
}