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
    [Description("Update the display name or description of a Microsoft Teams channel.")]
    [McpServerTool(Title = "Update Microsoft Teams channel", Name = "graph_teams_update_channel",
        Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_UpdateChannel(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel.")] string channelId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("New channel display name. Leave empty to keep unchanged.")] string? displayName = null,
        [Description("New channel description. Leave empty to keep unchanged.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (displayName is null && description is null)
                throw new ValidationException("A display name or description must be provided.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateTeamChannel { DisplayName = displayName, Description = description },
                cancellationToken);
            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            return await client.Teams[teamId].Channels[channelId].PatchAsync(
                new Channel { DisplayName = typed?.DisplayName, Description = typed?.Description },
                cancellationToken: cancellationToken);
        })));

    [Description("Delete a channel from a Microsoft Team.")]
    [McpServerTool(Title = "Delete Microsoft Teams channel", Name = "graph_teams_delete_channel",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_DeleteChannel(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel to delete.")] string channelId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteTeamChannel>(
            channelId,
            async _ => await client.Teams[teamId].Channels[channelId].DeleteAsync(cancellationToken: cancellationToken),
            "Microsoft Teams channel deleted.", cancellationToken)));

    [Description("Create a new channel in a Microsoft Teams.")]
    [McpServerTool(Title = "Create channel in Microsoft Team",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_CreateChannel(
        string teamId,
         [Description("Displayname of the new channel")]
        string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Membership type of the new channel ('standard', 'private' or 'shared')")]
        string? membershipType = "standard",
        [Description("Description of the new channel")]
        string? description = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
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

        var newItem = new Channel
        {
            DisplayName = typed.DisplayName,
            Description = typed.Description,
            MembershipType = typed.MembershipType
        };

        return await client.Teams[teamId].Channels.PostAsync(newItem, cancellationToken: cancellationToken);
    }));

    [Description("Create a new channel message in a Microsoft Teams channel.")]
    [McpServerTool(Title = "Create message in Teams channel",
        Destructive = true,    
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_CreateChannelMessage(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel.")] string channelId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Subject of the message.")] string? subject = null,
        [Description("Content (body) of the message.")] string? content = null,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
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
            Body = new ChatMessageBody
            {
                Content = typed?.Content,
            },
        };

        return await client.Teams[teamId]
            .Channels[channelId]
            .Messages
            .PostAsync(newItem, cancellationToken: cancellationToken);
    }));

    [Description("Create a reply to a Teams channel message, mentioning specified users.")]
    [McpServerTool(Title = "Reply in Teams channel with mentions",
        Destructive = true,     
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphTeams_ReplyWithMentions(
        [Description("ID of the Team.")] string teamId,
        [Description("ID of the Channel.")] string channelId,
        [Description("ID of the message to reply to.")] string messageId,
        [Description("IDs of the users to mention.")] List<string> mentionUserIds,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra message after mentions.")] string? content = null,
        CancellationToken cancellationToken = default)
         => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
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

        return await client.Teams[teamId]
            .Channels[channelId]
            .Messages[messageId]
            .Replies
            .PostAsync(newReply, cancellationToken: cancellationToken);
    })));

    [Description("Please fill in the Microsoft Teams channel fields to update.")]
    public sealed class GraphUpdateTeamChannel
    {
        [JsonPropertyName("displayName")]
        [Description("New channel display name.")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        [Description("New channel description.")]
        public string? Description { get; set; }
    }

    [Description("Please confirm the Microsoft Teams channel id to delete: {0}")]
    public sealed class GraphDeleteTeamChannel : MCPhappey.Common.Models.IHasName
    {
        [Required]
        [Description("Microsoft Teams channel id.")]
        public string Name { get; set; } = default!;
    }
}
