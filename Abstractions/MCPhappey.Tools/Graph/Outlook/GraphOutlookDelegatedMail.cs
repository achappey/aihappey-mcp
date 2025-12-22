using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookDelegatedMail
{
    [Description("Add a single category to an existing delegated email message (without removing existing ones).")]
    [McpServerTool(
        Title = "Add category to delegated email",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_AddCategory(
        [Description("Delegated user ID or mailbox address.")] string userId,
        [Description("The unique message ID of the email.")] string messageId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The category name to add. Must match an existing Outlook category name.")] string? category = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookMail.GraphMailSingleCategoryInput { Category = category ?? string.Empty },
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(typed?.Category))
                throw new ArgumentException("Category name cannot be empty.", nameof(category));

            var message = await client.Users[userId].Messages[messageId]
                .GetAsync(req => req.QueryParameters.Select = new[] { "categories" }, cancellationToken);

            var current = message?.Categories?.ToList() ?? [];

            if (!current.Contains(typed.Category, StringComparer.OrdinalIgnoreCase))
            {
                current.Add(typed.Category);

                await client.Users[userId].Messages[messageId]
                    .PatchAsync(new Message { Categories = current }, cancellationToken: cancellationToken);
            }

            return new
            {
                MessageId = messageId,
                Added = typed.Category,
                CurrentCategories = current,
                Status = current.Contains(typed.Category, StringComparer.OrdinalIgnoreCase)
                    ? "Category added successfully."
                    : "Category already existed."
            };
        })));


    [Description("Search for e-mails in a delegated Outlook mailbox using Microsoft Graph. Supports subject, body, sender, and date filters.")]
    [McpServerTool(Title = "Search delegated e-mails",
        Name = "graph_outlook_delegated_mail_search",
        OpenWorld = true, Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> GraphDelegatedMail_Search(
       [Description("Delegated user ID or mailbox address.")] string userId,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Search query, e.g. 'subject:AI from:sender@company.com hasAttachment:true'")] string query,
       [Description("Maximum number of results to return. Defaults to 10.")] int? top = 10,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        await client.Users[userId].Messages
                .GetAsync(opt =>
                {
                    opt.QueryParameters.Search = $"\"{query}\"";
                    opt.QueryParameters.Top = top ?? 10;
                    opt.QueryParameters.Select = [
                        "id", "subject", "from", "bodyPreview",
                        "receivedDateTime", "isRead", "webLink"
                    ];
                }, cancellationToken))));


    [Description("Set or update the follow-up flag for a delegated mail message.")]
    [McpServerTool(Title = "Flag Delegated Mail for Follow-up",
        Idempotent = true,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> GraphDelegatedMail_FlagMail(
        [Description("Delegated user ID or mailbox address.")] string userId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("ID of the message to flag.")][Required] string messageId,
        [Description("Flag status. Use Flagged, Complete, or NotFlagged. Defaults to Flagged.")]
            GraphOutlookMail.FlagStatusEnum? flagStatus = GraphOutlookMail.FlagStatusEnum.Flagged,
        [Description("Start date/time for the flag in ISO format (optional).")] string? startDateTime = null,
        [Description("Due date/time for the flag in ISO format (optional).")] string? dueDateTime = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphOutlookMail.GraphFlagMail
            {
                FlagStatus = flagStatus ?? GraphOutlookMail.FlagStatusEnum.Flagged,
                StartDateTime = startDateTime != null ? DateTimeOffset.Parse(startDateTime) : null,
                DueDateTime = dueDateTime != null ? DateTimeOffset.Parse(dueDateTime) : null,
            },
            cancellationToken
        );

        var flag = new FollowupFlag
        {
            FlagStatus = typed?.FlagStatus switch
            {
                GraphOutlookMail.FlagStatusEnum.Flagged => FollowupFlagStatus.Flagged,
                GraphOutlookMail.FlagStatusEnum.Complete => FollowupFlagStatus.Complete,
                _ => FollowupFlagStatus.NotFlagged
            }
        };

        if (typed?.StartDateTime.HasValue == true)
        {
            flag.StartDateTime = new DateTimeTimeZone
            {
                DateTime = typed.StartDateTime.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = typed.StartDateTime.Value.ToDateTimeTimeZone().TimeZone
            };
        }

        if (typed?.DueDateTime.HasValue == true)
        {
            flag.DueDateTime = new DateTimeTimeZone
            {
                DateTime = typed.DueDateTime.Value.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = typed.DueDateTime.Value.ToDateTimeTimeZone().TimeZone
            };
        }

        await client.Users[userId].Messages[messageId].PatchAsync(
            new Message { Flag = flag },
            cancellationToken: cancellationToken);

        return typed;
    })));


    [Description("Reply to an e-mail message in a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Reply to Delegated E-mail")]
    public static async Task<CallToolResult?> GraphDelegatedMail_Reply(
       [Description("Delegated user ID or mailbox address.")] string userId,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("ID of the message to reply to.")][Required] string messageId,
       [Description("Reply type: Reply or ReplyAll. Defaults to Reply.")] GraphOutlookMail.ReplyTypeEnum? replyType = GraphOutlookMail.ReplyTypeEnum.Reply,
       [Description("Content of the reply message.")] string? content = null,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphOutlookMail.GraphReplyMail
            {
                Comment = content ?? string.Empty,
                ReplyType = replyType ?? GraphOutlookMail.ReplyTypeEnum.Reply,
            },
            cancellationToken
        );

        if (typed.ReplyType == GraphOutlookMail.ReplyTypeEnum.ReplyAll)
        {
            await client.Users[userId].Messages[messageId].ReplyAll.PostAsync(
                new Microsoft.Graph.Beta.Users.Item.Messages.Item.ReplyAll.ReplyAllPostRequestBody
                { Comment = typed.Comment },
                cancellationToken: cancellationToken);
        }
        else
        {
            await client.Users[userId].Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Beta.Users.Item.Messages.Item.Reply.ReplyPostRequestBody
                { Comment = typed.Comment },
                cancellationToken: cancellationToken);
        }

        return typed.ToJsonContentBlock($"https://graph.microsoft.com/beta/users/{userId}/messages/{messageId}/reply")
             .ToCallToolResult();
    }));

    [Description("Send an e-mail message through a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Send delegated e-mail", Destructive = true)]
    public static async Task<CallToolResult?> GraphDelegatedMail_SendMail(
     [Description("Delegated user ID or mailbox address.")] string userId,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")] string? toRecipients = null,
     [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")] string? ccRecipients = null,
     [Description("Subject of the e-mail message.")] string? subject = null,
     [Description("Body of the e-mail message.")] string? body = null,
     [Description("Type of the message body (html or text).")] BodyType? bodyType = null,
     CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphOutlookMail.GraphSendMail
            {
                ToRecipients = toRecipients ?? string.Empty,
                CcRecipients = ccRecipients,
                Subject = subject,
                Body = body,
                BodyType = bodyType ?? BodyType.Text
            },
            cancellationToken
        );

        var newMessage = new Message
        {
            Subject = typed?.Subject,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = typed?.Body
            },
            ToRecipients = typed?.ToRecipients.Split(",").Select(a => a.ToRecipient()).ToList(),
            CcRecipients = typed?.CcRecipients?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.ToRecipient()).ToList() ?? []
        };

        var sendMailRequest = new Microsoft.Graph.Beta.Users.Item.SendMail.SendMailPostRequestBody
        {
            Message = newMessage,
            SaveToSentItems = true
        };

        await client.Users[userId].SendMail.PostAsync(sendMailRequest, cancellationToken: cancellationToken);
        return sendMailRequest;
    })));


    [Description("Create a draft e-mail message in a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Create Delegated Draft E-mail",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_CreateDraft(
        [Description("Delegated user ID or mailbox address.")] string userId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")] string? toRecipients = null,
        [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")] string? ccRecipients = null,
        [Description("Subject of the draft e-mail message.")] string? subject = null,
        [Description("Body of the draft e-mail message.")] string? body = null,
        [Description("Type of the message body (html or text).")] BodyType? bodyType = null,
        CancellationToken cancellationToken = default)
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphOutlookMail.GraphCreateMailDraft
            {
                ToRecipients = toRecipients ?? string.Empty,
                CcRecipients = ccRecipients,
                Subject = subject,
                Body = body,
                BodyType = bodyType ?? BodyType.Text
            },
            cancellationToken
        );

        var newMessage = new Message
        {
            Subject = typed?.Subject,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = typed?.Body
            },
            ToRecipients = typed?.ToRecipients
                ?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.ToRecipient())
                .ToList() ?? [],
            CcRecipients = typed?.CcRecipients
                ?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.ToRecipient())
                .ToList() ?? []
        };

        var client = await serviceProvider.GetOboGraphClient(requestContext.Server);
        var createdMessage = await client.Users[userId].Messages.PostAsync(newMessage, cancellationToken: cancellationToken);
        return createdMessage.ToJsonContentBlock($"https://graph.microsoft.com/beta/users/{userId}/messages/{createdMessage?.Id}")
            .ToCallToolResult();
    }
}