using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
                .GetAsync(req => req.QueryParameters.Select = ["categories"], cancellationToken);

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


    [Description("Move a single email message in a delegated Outlook mailbox to another mail folder in that mailbox. Requires explicit confirmation.")]
    [McpServerTool(
        Title = "Move delegated Outlook e-mail to folder",
        Name = "graph_outlook_delegated_mail_move_to_folder",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_MoveToFolder(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The unique message ID of the delegated email to move.")][Required] string messageId,
        [Description("The destination mail folder ID in the delegated mailbox. Use Microsoft Graph mailFolder IDs, or a well-known folder name such as inbox, archive, deleteditems, junkemail, or sentitems.")][Required] string destinationFolderId,
        [Description("Optional expected destination folder display name. When supplied, it is validated against the resolved folder.")] string? destinationFolderDisplayName = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var destination = await GraphOutlookMail.ResolveDelegatedMailFolderAsync(client, userId, destinationFolderId, destinationFolderDisplayName, cancellationToken);
            var message = await client.Users[userId].Messages[messageId].GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = GraphOutlookMail.MailMoveMessageSelect;
            }, cancellationToken) ?? throw new ValidationException($"Message '{messageId}' was not found in delegated mailbox '{userId}'.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookMail.GraphMailMoveConfirmationInput
                {
                    Confirm = false,
                    Mailbox = userId,
                    MessageCount = 1,
                    DestinationFolderId = destination.Id,
                    DestinationFolderDisplayName = destination.DisplayName,
                    Preview = GraphOutlookMail.FormatMovePreview([message])
                },
                cancellationToken);

            if (notAccepted != null) throw new Exception(System.Text.Json.JsonSerializer.Serialize(notAccepted));
            if (typed?.Confirm != true) throw new ValidationException("Move was not confirmed.");

            return await GraphOutlookMail.MoveDelegatedMessagesAsync(
                requestContext,
                client,
                userId,
                [message],
                destination,
                null,
                cancellationToken);
        })));

    [Description("Move delegated Outlook email messages matching a safe hybrid search (keywords plus primitive filters) to another mail folder in that delegated mailbox. Requires explicit confirmation after preview.")]
    [McpServerTool(
        Title = "Move delegated Outlook e-mails by safe search to folder",
        Name = "graph_outlook_delegated_mail_move_search_to_folder",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_MoveSearchToFolder(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The destination mail folder ID in the delegated mailbox. Use Microsoft Graph mailFolder IDs, or a well-known folder name such as inbox, archive, deleteditems, junkemail, or sentitems.")][Required] string destinationFolderId,
        [Description("Optional keyword search across the delegated message index. Do not pass OData syntax; this value is escaped and translated server-side.")] string? keywords = null,
        [Description("Optional exact sender email address filter.")] string? fromAddress = null,
        [Description("Optional subject contains filter. This is translated to a safe Microsoft Graph contains(subject, ...) filter.")] string? subjectContains = null,
        [Description("Optional lower bound for receivedDateTime, in ISO 8601 format.")] string? receivedAfter = null,
        [Description("Optional upper bound for receivedDateTime, in ISO 8601 format.")] string? receivedBefore = null,
        [Description("When true, only unread messages are moved.")] bool? unreadOnly = null,
        [Description("When set, filters messages by attachment presence.")] bool? hasAttachments = null,
        [Description("Optional expected destination folder display name. When supplied, it is validated against the resolved folder.")] string? destinationFolderDisplayName = null,
        [Description("Maximum number of messages to move. Defaults to 25 and is capped at 100.")] int? maxMessages = 25,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var query = GraphOutlookMail.BuildSafeMailMoveQuery(keywords, fromAddress, subjectContains, receivedAfter, receivedBefore, unreadOnly, hasAttachments, maxMessages);
            var destination = await GraphOutlookMail.ResolveDelegatedMailFolderAsync(client, userId, destinationFolderId, destinationFolderDisplayName, cancellationToken);

            var messagesResponse = await client.Users[userId].Messages.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Top = query.Top;
                requestConfiguration.QueryParameters.Select = GraphOutlookMail.MailMoveMessageSelect;
                requestConfiguration.QueryParameters.Orderby = ["receivedDateTime DESC"];
                if (!string.IsNullOrWhiteSpace(query.Filter))
                    requestConfiguration.QueryParameters.Filter = query.Filter;
                else if (!string.IsNullOrWhiteSpace(query.Search))
                    requestConfiguration.QueryParameters.Search = query.Search;
            }, cancellationToken);

            var messages = messagesResponse?.Value?.Where(message => !string.IsNullOrWhiteSpace(message.Id)).Take(query.Top).ToList() ?? [];
            messages = GraphOutlookMail.ApplyClientSideKeywordFallback(messages, query).Take(query.Top).ToList();

            if (messages.Count == 0)
            {
                return new GraphOutlookMail.GraphMailMoveResult
                {
                    Mailbox = userId,
                    DestinationFolderId = destination.Id,
                    DestinationFolderDisplayName = destination.DisplayName,
                    Requested = 0,
                    Attempted = 0,
                    Moved = 0,
                    Failed = 0,
                    Query = query,
                    Messages = []
                };
            }

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphOutlookMail.GraphMailMoveConfirmationInput
                {
                    Confirm = false,
                    Mailbox = userId,
                    MessageCount = messages.Count,
                    DestinationFolderId = destination.Id,
                    DestinationFolderDisplayName = destination.DisplayName,
                    Keywords = keywords,
                    FromAddress = fromAddress,
                    SubjectContains = subjectContains,
                    ReceivedAfter = receivedAfter,
                    ReceivedBefore = receivedBefore,
                    UnreadOnly = unreadOnly,
                    HasAttachments = hasAttachments,
                    Preview = GraphOutlookMail.FormatMovePreview(messages.Take(10))
                },
                cancellationToken);

            if (notAccepted != null) throw new Exception(System.Text.Json.JsonSerializer.Serialize(notAccepted));
            if (typed?.Confirm != true) throw new ValidationException("Move was not confirmed.");

            return await GraphOutlookMail.MoveDelegatedMessagesAsync(requestContext, client, userId, messages, destination, query, cancellationToken);
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
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")] string? toRecipients = null,
     [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")] string? ccRecipients = null,
     [Description("Subject of the e-mail message.")] string? subject = null,
     [Description("Body of the e-mail message.")] string? body = null,
     [Description("Type of the message body (html or text).")] BodyType? bodyType = null,
     [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the body.")] string? emailSignatureUrl = null,
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
                BodyType = bodyType ?? BodyType.Text,
                EmailSignatureUrl = emailSignatureUrl
            },
            cancellationToken
        );

        var resolvedBody = await GraphOutlookMail.BuildBodyWithOptionalSignatureAsync(
            serviceProvider,
            requestContext,
            typed?.Body,
            typed?.BodyType,
            typed?.EmailSignatureUrl,
            cancellationToken);

        var newMessage = new Message
        {
            Subject = typed?.Subject,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = resolvedBody
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
        [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the draft body.")] string? emailSignatureUrl = null,
        CancellationToken cancellationToken = default)
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphOutlookMail.GraphCreateMailDraft
            {
                ToRecipients = toRecipients ?? string.Empty,
                CcRecipients = ccRecipients,
                Subject = subject,
                Body = body,
                BodyType = bodyType ?? BodyType.Text,
                EmailSignatureUrl = emailSignatureUrl
            },
            cancellationToken
        );

        var resolvedBody = await GraphOutlookMail.BuildBodyWithOptionalSignatureAsync(
            serviceProvider,
            requestContext,
            typed?.Body,
            typed?.BodyType,
            typed?.EmailSignatureUrl,
            cancellationToken);

        var newMessage = new Message
        {
            Subject = typed?.Subject,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = resolvedBody
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
