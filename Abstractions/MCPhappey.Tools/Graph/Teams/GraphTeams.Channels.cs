using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Teams;

public static partial class GraphTeams
{
    [Description("Create a new channel in a Microsoft Teams.")]
    [McpServerTool(Title = "Create channel in Microsoft Team", Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_CreateChannel(
        string teamId,
         [Description("Displayname of the new channel")]
        string displayName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Membership type of the new channel ('standard', 'private' or 'shared')")]
        string? membershipType = "standard",
        [Description("Description of the new channel")]
        string? description = null,
        CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
    {
        var teams = await client.Teams[teamId]
                           .GetAsync(cancellationToken: cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewTeamChannel
            {
                DisplayName = displayName,
                Description = description,
                MembershipType = membershipType == "shared"
                    ? ChannelMembershipType.Shared
                    : membershipType == "private" ?
                    ChannelMembershipType.Private : ChannelMembershipType.Standard
            },
            cancellationToken
        );

        if (notAccepted != null) return notAccepted;
        if (typed == null) return "Invalid result".ToErrorCallToolResponse();

        var newItem = new Channel
        {
            DisplayName = typed.DisplayName,
            Description = typed.Description,
            MembershipType = typed.MembershipType
        };

        var graphItem = await client.Teams[teamId].Channels.PostAsync(newItem, cancellationToken: cancellationToken);

        return (graphItem ?? newItem).ToJsonContentBlock($"https://graph.microsoft.com/beta/teams/{teamId}/channels")
            .ToCallToolResult();

    }));

    [Description("Create a new channel message in a Microsoft Teams channel.")]
    [McpServerTool(Title = "Create message in Teams channel",
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_CreateChannelMessage(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel.")] string channelId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Subject of the message.")] string? subject = null,
        [Description("Content (body) of the message.")] string? content = null,
        CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
    {
        // Vul defaults uit de parameters direct in
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewChannelMessage
            {
                Subject = subject,
                Content = content,
                Importance = ChatMessageImportance.Normal
            },
            cancellationToken
        );

        var newItem = new ChatMessage
        {
            Subject = typed?.Subject,
            Importance = typed?.Importance,
            Body = new ItemBody
            {
                Content = typed?.Content,
            },
        };

        var graphItem = await client.Teams[teamId]
            .Channels[channelId]
            .Messages
            .PostAsync(newItem, cancellationToken: cancellationToken);

        return (graphItem ?? newItem)
            .ToJsonContentBlock($"https://graph.microsoft.com/beta/teams/{teamId}/channels/{channelId}/messages")
            .ToCallToolResult();

    }));

    [Description("Create a reply to a Teams channel message, mentioning specified users.")]
    [McpServerTool(Title = "Reply in Teams channel with mentions",
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_ReplyWithMentions(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel.")] string channelId,
        [Description("ID of the message to reply to.")] string messageId,
        [Description("IDs of the users to mention.")] List<string> mentionUserIds,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra message after mentions.")] string? content = null,
        CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
    {
        var mentionInfo = new List<(string Id, string DisplayName)>();
        foreach (var userId in mentionUserIds)
        {
            var user = await client.Users[userId].GetAsync(cancellationToken: cancellationToken);
            mentionInfo.Add((userId, user?.DisplayName ?? userId));
        }

        var mentionList = string.Join("\n", mentionInfo.Select(x => $"- {x.DisplayName}"));
        var elicit = await requestContext.Server.ElicitAsync(new ElicitRequestParams()
        {
            Message = mentionList
        }, cancellationToken: cancellationToken);

        if (elicit.Action != "accept")
        {
            return elicit.Action.ToErrorCallToolResponse();
        }

        // Resolve display names for user IDs (helper function, see below)
        var mentions = new List<ChatMessageMention>();
        var mentionTags = new List<string>();

        int idx = 0;
        foreach (var (userId, displayName) in mentionInfo)
        {
            mentionTags.Add($"<at id=\"{idx}\">{displayName}</at>");
            mentions.Add(new ChatMessageMention
            {
                Id = idx,
                MentionText = displayName,
                Mentioned = new ChatMessageMentionedIdentitySet
                {
                    User = new Identity
                    {
                        Id = userId,
                        DisplayName = displayName
                    }
                }
            });
            idx++;
        }

        var bodyContent = string.Join(", ", mentionTags);
        if (!string.IsNullOrWhiteSpace(content))
            bodyContent += " " + content;

        ChatMessage newReply = new()
        {
            Body = new()
            {
                ContentType = BodyType.Html,
                Content = bodyContent
            },
            Mentions = mentions
        };

        var result = await client.Teams[teamId]
            .Channels[channelId]
            .Messages[messageId]
            .Replies
            .PostAsync(newReply, cancellationToken: cancellationToken);

        return (result ?? newReply)
            .ToJsonContentBlock($"https://graph.microsoft.com/beta/teams/{teamId}/channels/{channelId}/messages/{messageId}/replies")
            .ToCallToolResult();

    }));


}