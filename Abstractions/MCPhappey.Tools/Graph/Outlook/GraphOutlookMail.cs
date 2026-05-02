using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookMail
{
    private const int DefaultMoveSearchLimit = 25;
    private const int MaxMoveSearchLimit = 100;

    [Description("Add a single category to an existing email message in Outlook (without removing existing ones).")]
    [McpServerTool(
        Title = "Add Category to Email",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphMail_AddCategory(
        [Description("The unique message ID of the email.")] string messageId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The category name to add. Must match an existing Outlook category name.")] string? category = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            // Let AI or user confirm the category name
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphMailSingleCategoryInput { Category = category ?? string.Empty },
                cancellationToken
            );

            if (notAccepted != null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            if (string.IsNullOrWhiteSpace(typed?.Category))
                throw new ArgumentException("Category name cannot be empty.", nameof(category));

            // Step 1: Get current categories for the message
            var message = await client.Me.Messages[messageId]
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = ["categories"];
                }, cancellationToken);

            var current = message?.Categories?.ToList() ?? [];

            // Step 2: Append new category if not already present
            if (!current.Contains(typed.Category, StringComparer.OrdinalIgnoreCase))
            {
                current.Add(typed.Category);

                await client.Me.Messages[messageId]
                    .PatchAsync(new Message
                    {
                        Categories = current
                    }, cancellationToken: cancellationToken);
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

    [Description("Please provide the category name to add to the email.")]
    public class GraphMailSingleCategoryInput
    {
        [JsonPropertyName("category")]
        [Required]
        [Description("The single category name to add.")]
        public string Category { get; set; } = default!;
    }

    [Description("Move a single Outlook email message to another mail folder in the current user's mailbox. Requires explicit confirmation.")]
    [McpServerTool(
        Title = "Move Outlook e-mail to folder",
        Name = "graph_outlook_mail_move_to_folder",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_MoveToFolder(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The unique message ID of the email to move.")][Required] string messageId,
        [Description("The destination mail folder ID. Use Microsoft Graph mailFolder IDs, or a well-known folder name such as inbox, archive, deleteditems, junkemail, or sentitems.")][Required] string destinationFolderId,
        [Description("Optional expected destination folder display name. When supplied, it is validated against the resolved folder.")] string? destinationFolderDisplayName = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var destination = await ResolveCurrentUserMailFolderAsync(client, destinationFolderId, destinationFolderDisplayName, cancellationToken);
            var message = await client.Me.Messages[messageId].GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = MailMoveMessageSelect;
            }, cancellationToken) ?? throw new ValidationException($"Message '{messageId}' was not found.");

            var confirmation = new GraphMailMoveConfirmationInput
            {
                Mailbox = "me",
                MessageCount = 1,
                DestinationFolderId = destination.Id,
                DestinationFolderDisplayName = destination.DisplayName,
                Preview = FormatMovePreview([message])
            };

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(confirmation, cancellationToken);
            if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));
            if (typed == null) throw new ValidationException("Move was not confirmed.");

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter: 0,
                message: $"Moving 1 message to '{destination.DisplayName}'...",
                total: 1,
                cancellationToken: cancellationToken);

            var moved = await client.Me.Messages[messageId].Move.PostAsync(
                new Microsoft.Graph.Beta.Me.Messages.Item.Move.MovePostRequestBody
                {
                    DestinationId = destination.Id
                },
                cancellationToken: cancellationToken);

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter: 1,
                message: $"Moved message '{message.Subject}'.",
                total: 1,
                cancellationToken: cancellationToken);

            return new GraphMailMoveResult
            {
                Mailbox = "me",
                DestinationFolderId = destination.Id,
                DestinationFolderDisplayName = destination.DisplayName,
                Requested = 1,
                Attempted = 1,
                Moved = 1,
                Failed = 0,
                Messages =
                [
                    new GraphMailMoveItemResult
                    {
                        OriginalMessageId = messageId,
                        MovedMessageId = moved?.Id,
                        Subject = message.Subject,
                        From = message.From?.EmailAddress?.Address,
                        ReceivedDateTime = message.ReceivedDateTime,
                        Status = "Moved"
                    }
                ]
            };
        })));

    [Description("Filter e-mails in Outlook using Microsoft Graph OData filter. Exact matching on fields like receivedDateTime, sender, subject. Use for deterministic filtering (dates, sender, flags).")]
    [McpServerTool(
        Title = "Filter e-mails in Outlook (exact)",
        Name = "graph_outlook_mail_filter",
        OpenWorld = true,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> GraphOutlookMail_Filter(
         RequestContext<CallToolRequestParams> requestContext,
         [Description("OData filter, e.g. receivedDateTime ge 2026-03-31T00:00:00Z and from/emailAddress/address eq 'user@company.com'")]
    string filter,
         [Description("Maximum number of results to return. Defaults to 10.")]
    int? top = 10,
         CancellationToken cancellationToken = default) =>
         await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async client =>
         await requestContext.WithStructuredContent(async () =>
         await client.Me.Messages
             .GetAsync(opt =>
             {
                 opt.QueryParameters.Filter = filter;
                 opt.QueryParameters.Top = top ?? 10;
                 opt.QueryParameters.Orderby = ["receivedDateTime DESC"];
                 opt.QueryParameters.Select =
                 [
                     "id",
                    "subject",
                    "from",
                    "bodyPreview",
                    "receivedDateTime",
                    "isRead",
                    "webLink"
                 ];
             }, cancellationToken))));


    [Description("Search e-mails in Outlook using Microsoft Graph search. Fuzzy full-text search across subject, body, sender. Use for keyword queries, not exact date filtering.")]
    [McpServerTool(
        Title = "Search e-mails in Outlook (text search)",
        Name = "graph_outlook_mail_search",
        OpenWorld = true,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> GraphOutlookMail_Search(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query, e.g. 'subject:AI from:sender@company.com hasAttachment:true'")] string query,
        [Description("Maximum number of results to return. Defaults to 10.")] int? top = 10,
        CancellationToken cancellationToken = default) =>
         await requestContext.WithExceptionCheck(async () =>
         await requestContext.WithOboGraphClient(async client =>
         await requestContext.WithStructuredContent(async () =>
         await client.Me.Messages
                 .GetAsync(opt =>
                 {
                     opt.QueryParameters.Search = $"\"{query}\"";
                     opt.QueryParameters.Top = top ?? 10;
                     opt.QueryParameters.Select = ["id", "subject", "from", "bodyPreview", "receivedDateTime", "isRead", "webLink"];
                 }, cancellationToken))));

    [Description("Set or update the follow-up flag for a mail message in Outlook.")]
    [McpServerTool(Title = "Flag mail for follow-up in Outlook",
        Idempotent = true,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> GraphOutlookMail_FlagMail(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("ID of the message to flag.")][Required] string messageId,
        [Description("Flag status. Use Flagged, Complete, or NotFlagged. Defaults to Flagged.")]
            FlagStatusEnum? flagStatus = FlagStatusEnum.Flagged,
        [Description("Start date/time for the flag in ISO format (optional).")] string? startDateTime = null,
        [Description("Due date/time for the flag in ISO format (optional).")] string? dueDateTime = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphFlagMail
            {
                FlagStatus = flagStatus ?? FlagStatusEnum.Flagged,
                StartDateTime = startDateTime != null ? DateTimeOffset.Parse(startDateTime) : null,
                DueDateTime = dueDateTime != null ? DateTimeOffset.Parse(dueDateTime) : null,
            },
            cancellationToken
        );

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        var flag = new FollowupFlag
        {
            FlagStatus = typed?.FlagStatus switch
            {
                FlagStatusEnum.Flagged => FollowupFlagStatus.Flagged,
                FlagStatusEnum.Complete => FollowupFlagStatus.Complete,
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

        var updatedMessage = new Message
        {
            Flag = flag
        };

        await client.Me.Messages[messageId].PatchAsync(updatedMessage, cancellationToken: cancellationToken);

        return typed;
    })));

    public enum FlagStatusEnum
    {
        [Description("Not flagged")]
        NotFlagged,
        [Description("Flagged (for follow-up)")]
        Flagged,
        [Description("Complete")]
        Complete
    }

    [Description("Please fill in the mail flagging details")]
    public class GraphFlagMail
    {
        [JsonPropertyName("flagStatus")]
        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Flag status. Use Flagged, Complete, or NotFlagged. Defaults to Flagged.")]
        public FlagStatusEnum FlagStatus { get; set; } = FlagStatusEnum.Flagged;

        [JsonPropertyName("startDateTime")]
        [Description("Start date/time for the flag in ISO format (optional).")]
        public DateTimeOffset? StartDateTime { get; set; }

        [JsonPropertyName("dueDateTime")]
        [Description("Due date/time for the flag in ISO format (optional).")]
        public DateTimeOffset? DueDateTime { get; set; }

    }


    [Description("Reply to an e-mail message in Outlook.")]
    [McpServerTool(Title = "Reply to e-mail via Outlook")]
    public static async Task<CallToolResult?> GraphOutlookMail_Reply(
       RequestContext<CallToolRequestParams> requestContext,
       [Description("ID of the message to reply to.")][Required] string messageId,
       [Description("Reply type: Reply or ReplyAll. Defaults to Reply.")] ReplyTypeEnum? replyType = ReplyTypeEnum.Reply,
       [Description("Content of the reply message.")] string? content = null,
       CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphReplyMail
            {
                Comment = content ?? string.Empty,
                ReplyType = replyType ?? ReplyTypeEnum.Reply,
            },
            cancellationToken
        );

        if (typed?.ReplyType == ReplyTypeEnum.ReplyAll)
        {
            await client.Me.Messages[messageId].ReplyAll.PostAsync(
                new Microsoft.Graph.Beta.Me.Messages.Item.ReplyAll.ReplyAllPostRequestBody { Comment = typed.Comment },
                cancellationToken: cancellationToken);
        }
        else
        {
            await client.Me.Messages[messageId].Reply.PostAsync(
                new Microsoft.Graph.Beta.Me.Messages.Item.Reply.ReplyPostRequestBody { Comment = typed?.Comment },
                cancellationToken: cancellationToken);
        }

        return typed.ToJsonContentBlock($"https://graph.microsoft.com/beta/me/messages/{messageId}/reply")
             .ToCallToolResult();
    }));

    public enum ReplyTypeEnum
    {
        Reply,
        ReplyAll
    }

    [Description("Please fill in the reply details")]
    public class GraphReplyMail
    {
        [JsonPropertyName("replyType")]
        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Reply type: Reply or ReplyAll. Defaults to Reply.")]
        public ReplyTypeEnum ReplyType { get; set; } = ReplyTypeEnum.Reply;

        [JsonPropertyName("comment")]
        [Required]
        [Description("Reply content")]
        public string Comment { get; set; } = string.Empty;
    }

    [Description("Send an e-mail message through Outlook from the current users' mailbox.")]
    [McpServerTool(Title = "Send e-mail via Outlook", Destructive = true)]
    public static async Task<CallToolResult?> GraphOutlookMail_SendMail(
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")] string? toRecipients = null,
     [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")] string? ccRecipients = null,
     [Description("Subject of the e-mail message.")] string? subject = null,
     [Description("Body of the e-mail message.")] string? body = null,
     [Description("Type of the message body (html or text).")] BodyType? bodyType = null,
     [Description("Importance.")] Importance? importance = null,
     [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the body.")] string? emailSignatureUrl = null,
     CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphSendMail
            {
                ToRecipients = toRecipients ?? string.Empty,
                CcRecipients = ccRecipients,
                Subject = subject,
                Body = body,
                Importance = importance,
                BodyType = bodyType ?? BodyType.Text,
                EmailSignatureUrl = emailSignatureUrl
            },
            cancellationToken
        );

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        var resolvedBody = await BuildBodyWithOptionalSignatureAsync(
            serviceProvider,
            requestContext,
            typed?.Body,
            typed?.BodyType,
            typed?.EmailSignatureUrl,
            cancellationToken);

        Message newMessage = new()
        {
            Subject = typed?.Subject,
            Importance = typed?.Importance,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = resolvedBody
            },
            ToRecipients = typed?.ToRecipients.Split(",").Select(a => a.ToRecipient()).ToList(),
            CcRecipients = typed?.CcRecipients?.Split(",", StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.ToRecipient())
                .ToList() ?? []
        };

        Microsoft.Graph.Beta.Me.SendMail.SendMailPostRequestBody sendMailPostRequestBody =
            new()
            {
                Message = newMessage,
                SaveToSentItems = true
            };

        await client.Me.SendMail.PostAsync(sendMailPostRequestBody, cancellationToken: cancellationToken);

        return sendMailPostRequestBody;
    })));

    [Description("Create a draft e-mail message in the current user's Outlook mailbox.")]
    [McpServerTool(Title = "Create draft e-mail in Outlook",
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_CreateDraft(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")] string? toRecipients = null,
        [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")] string? ccRecipients = null,
        [Description("Subject of the draft e-mail message.")] string? subject = null,
        [Description("Body of the draft e-mail message.")] string? body = null,
        [Description("Type of the message body (html or text).")] BodyType? bodyType = null,
        [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the draft body.")] string? emailSignatureUrl = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphCreateMailDraft
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

        if (notAccepted != null) return notAccepted;

        var resolvedBody = await BuildBodyWithOptionalSignatureAsync(
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

        var createdMessage = await client.Me.Messages.PostAsync(newMessage, cancellationToken: cancellationToken);
        return createdMessage.ToJsonContentBlock($"https://graph.microsoft.com/beta/me/messages/{createdMessage?.Id}").ToCallToolResult();
    }));

    internal static async Task<string?> BuildBodyWithOptionalSignatureAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? body,
        BodyType? bodyType,
        string? emailSignatureUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailSignatureUrl))
            return body;

        if ((bodyType ?? BodyType.Text) == BodyType.Text)
            throw new ValidationException("emailSignatureUrl can only be used when bodyType is html.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var signatureFiles = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            emailSignatureUrl,
            cancellationToken);

        var signatureFile = signatureFiles.FirstOrDefault()
            ?? throw new ValidationException("Failed to download content from emailSignatureUrl.");

        var signatureHtml = signatureFile.Contents.ToString();

        if (string.IsNullOrWhiteSpace(signatureHtml))
            throw new ValidationException("emailSignatureUrl returned empty content.");

        return $"{body ?? string.Empty}{signatureHtml}";
    }

    internal static readonly string[] MailMoveMessageSelect =
    [
        "id",
        "subject",
        "from",
        "receivedDateTime",
        "isRead",
        "hasAttachments",
        "webLink",
        "bodyPreview"
    ];

    internal static async Task<MailFolder> ResolveCurrentUserMailFolderAsync(
        GraphServiceClient client,
        string destinationFolderId,
        string? destinationFolderDisplayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolderId);

        var folder = await client.Me.MailFolders[NormalizeWellKnownFolderId(destinationFolderId)]
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount"];
            }, cancellationToken)
            ?? throw new ValidationException($"Destination mail folder '{destinationFolderId}' was not found.");

        ValidateDestinationFolderName(folder, destinationFolderDisplayName);
        return folder;
    }

    internal static async Task<MailFolder> ResolveDelegatedMailFolderAsync(
        GraphServiceClient client,
        string userId,
        string destinationFolderId,
        string? destinationFolderDisplayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolderId);

        var folder = await client.Users[userId].MailFolders[NormalizeWellKnownFolderId(destinationFolderId)]
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = ["id", "displayName", "parentFolderId", "totalItemCount"];
            }, cancellationToken)
            ?? throw new ValidationException($"Destination mail folder '{destinationFolderId}' was not found for mailbox '{userId}'.");

        ValidateDestinationFolderName(folder, destinationFolderDisplayName);
        return folder;
    }

    internal static GraphMailMoveQuery BuildSafeMailMoveQuery(
        string? keywords,
        string? fromAddress,
        string? subjectContains,
        string? receivedAfter,
        string? receivedBefore,
        bool? unreadOnly,
        bool? hasAttachments,
        int? maxMessages)
    {
        var top = Math.Clamp(maxMessages ?? DefaultMoveSearchLimit, 1, MaxMoveSearchLimit);
        var filterParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(fromAddress))
        {
            var trimmed = fromAddress.Trim();
            if (!new EmailAddressAttribute().IsValid(trimmed))
                throw new ValidationException("fromAddress must be a valid email address.");

            filterParts.Add($"from/emailAddress/address eq '{EscapeODataString(trimmed)}'");
        }

        if (!string.IsNullOrWhiteSpace(subjectContains))
            filterParts.Add($"contains(subject,'{EscapeODataString(TrimAndLimit(subjectContains, 120, nameof(subjectContains)))}')");

        if (TryParseDateTimeOffset(receivedAfter, nameof(receivedAfter), out var receivedAfterDate))
            filterParts.Add($"receivedDateTime ge {receivedAfterDate:O}");

        if (TryParseDateTimeOffset(receivedBefore, nameof(receivedBefore), out var receivedBeforeDate))
            filterParts.Add($"receivedDateTime le {receivedBeforeDate:O}");

        if (receivedAfterDate.HasValue && receivedBeforeDate.HasValue && receivedAfterDate > receivedBeforeDate)
            throw new ValidationException("receivedAfter must be earlier than or equal to receivedBefore.");

        if (unreadOnly == true)
            filterParts.Add("isRead eq false");

        if (hasAttachments.HasValue)
            filterParts.Add($"hasAttachments eq {hasAttachments.Value.ToString().ToLowerInvariant()}");

        var normalizedKeywords = NormalizeSearchKeywords(keywords);

        if (string.IsNullOrWhiteSpace(normalizedKeywords) && filterParts.Count == 0)
            throw new ValidationException("At least one safe search input is required: keywords, fromAddress, subjectContains, receivedAfter, receivedBefore, unreadOnly, or hasAttachments.");

        return new GraphMailMoveQuery
        {
            Keywords = normalizedKeywords,
            Search = string.IsNullOrWhiteSpace(normalizedKeywords) || filterParts.Count > 0 ? null : $"\"{EscapeGraphSearchString(normalizedKeywords)}\"",
            Filter = filterParts.Count == 0 ? null : string.Join(" and ", filterParts),
            Top = top,
            MaxAllowed = MaxMoveSearchLimit,
            ClientSideKeywordFallback = !string.IsNullOrWhiteSpace(normalizedKeywords) && filterParts.Count > 0
        };
    }

    internal static string FormatMovePreview(IEnumerable<Message> messages)
        => string.Join(Environment.NewLine, messages.Select((message, index) =>
            $"{index + 1}. {message.Subject ?? "(no subject)"} | from: {message.From?.EmailAddress?.Address ?? "unknown"} | received: {message.ReceivedDateTime:O} | id: {message.Id}"));

    internal static IEnumerable<Message> ApplyClientSideKeywordFallback(IEnumerable<Message> messages, GraphMailMoveQuery query)
    {
        if (query.ClientSideKeywordFallback != true || string.IsNullOrWhiteSpace(query.Keywords))
            return messages;

        var keyword = query.Keywords.Trim();
        return messages.Where(message =>
            (message.Subject?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
            || (message.BodyPreview?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
            || (message.From?.EmailAddress?.Address?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
            || (message.From?.EmailAddress?.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true));
    }

    internal static async Task<GraphMailMoveResult> MoveCurrentUserMessagesAsync(
        RequestContext<CallToolRequestParams> requestContext,
        GraphServiceClient client,
        IReadOnlyList<Message> messages,
        MailFolder destination,
        GraphMailMoveQuery? query,
        CancellationToken cancellationToken)
    {
        var results = new List<GraphMailMoveItemResult>();

        await requestContext.Server.SendProgressNotificationAsync(
            requestContext,
            progressCounter: 0,
            message: $"Moving {messages.Count} message(s) to '{destination.DisplayName}'...",
            total: messages.Count,
            cancellationToken: cancellationToken);

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            try
            {
                var moved = await client.Me.Messages[message.Id].Move.PostAsync(
                    new Microsoft.Graph.Beta.Me.Messages.Item.Move.MovePostRequestBody
                    {
                        DestinationId = destination.Id
                    },
                    cancellationToken: cancellationToken);

                results.Add(new GraphMailMoveItemResult
                {
                    OriginalMessageId = message.Id,
                    MovedMessageId = moved?.Id,
                    Subject = message.Subject,
                    From = message.From?.EmailAddress?.Address,
                    ReceivedDateTime = message.ReceivedDateTime,
                    Status = "Moved"
                });
            }
            catch (Exception ex)
            {
                results.Add(new GraphMailMoveItemResult
                {
                    OriginalMessageId = message.Id,
                    Subject = message.Subject,
                    From = message.From?.EmailAddress?.Address,
                    ReceivedDateTime = message.ReceivedDateTime,
                    Status = "Failed",
                    Error = ex.Message
                });
            }

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter: i + 1,
                message: $"Moved {results.Count(result => result.Status == "Moved")}/{messages.Count} message(s) to '{destination.DisplayName}'.",
                total: messages.Count,
                cancellationToken: cancellationToken);
        }

        return BuildMoveResult("me", destination, query, results, messages.Count);
    }

    internal static async Task<GraphMailMoveResult> MoveDelegatedMessagesAsync(
        RequestContext<CallToolRequestParams> requestContext,
        GraphServiceClient client,
        string userId,
        IReadOnlyList<Message> messages,
        MailFolder destination,
        GraphMailMoveQuery? query,
        CancellationToken cancellationToken)
    {
        var results = new List<GraphMailMoveItemResult>();

        await requestContext.Server.SendProgressNotificationAsync(
            requestContext,
            progressCounter: 0,
            message: $"Moving {messages.Count} message(s) in mailbox '{userId}' to '{destination.DisplayName}'...",
            total: messages.Count,
            cancellationToken: cancellationToken);

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            try
            {
                var moved = await client.Users[userId].Messages[message.Id].Move.PostAsync(
                    new Microsoft.Graph.Beta.Users.Item.Messages.Item.Move.MovePostRequestBody
                    {
                        DestinationId = destination.Id
                    },
                    cancellationToken: cancellationToken);

                results.Add(new GraphMailMoveItemResult
                {
                    OriginalMessageId = message.Id,
                    MovedMessageId = moved?.Id,
                    Subject = message.Subject,
                    From = message.From?.EmailAddress?.Address,
                    ReceivedDateTime = message.ReceivedDateTime,
                    Status = "Moved"
                });
            }
            catch (Exception ex)
            {
                results.Add(new GraphMailMoveItemResult
                {
                    OriginalMessageId = message.Id,
                    Subject = message.Subject,
                    From = message.From?.EmailAddress?.Address,
                    ReceivedDateTime = message.ReceivedDateTime,
                    Status = "Failed",
                    Error = ex.Message
                });
            }

            await requestContext.Server.SendProgressNotificationAsync(
                requestContext,
                progressCounter: i + 1,
                message: $"Moved {results.Count(result => result.Status == "Moved")}/{messages.Count} delegated message(s) to '{destination.DisplayName}'.",
                total: messages.Count,
                cancellationToken: cancellationToken);
        }

        return BuildMoveResult(userId, destination, query, results, messages.Count);
    }

    private static GraphMailMoveResult BuildMoveResult(
        string mailbox,
        MailFolder destination,
        GraphMailMoveQuery? query,
        IReadOnlyList<GraphMailMoveItemResult> results,
        int requested)
        => new()
        {
            Mailbox = mailbox,
            DestinationFolderId = destination.Id,
            DestinationFolderDisplayName = destination.DisplayName,
            Requested = requested,
            Attempted = results.Count,
            Moved = results.Count(result => result.Status == "Moved"),
            Failed = results.Count(result => result.Status == "Failed"),
            Query = query,
            Messages = results
        };

    private static void ValidateDestinationFolderName(MailFolder folder, string? destinationFolderDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(destinationFolderDisplayName)
            && !string.Equals(folder.DisplayName, destinationFolderDisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Resolved destination folder name '{folder.DisplayName}' does not match expected name '{destinationFolderDisplayName}'.");
        }
    }

    private static string NormalizeWellKnownFolderId(string folderId)
    {
        var trimmed = folderId.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "deleted items" => "deleteditems",
            "junk email" => "junkemail",
            "sent items" => "sentitems",
            "drafts" => "drafts",
            "inbox" => "inbox",
            "archive" => "archive",
            "deleteditems" => "deleteditems",
            "junkemail" => "junkemail",
            "sentitems" => "sentitems",
            _ => trimmed
        };
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeGraphSearchString(string value) => value.Replace("\\", " ", StringComparison.Ordinal).Replace("\"", " ", StringComparison.Ordinal);

    private static string TrimAndLimit(string? value, int maxLength, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ValidationException($"{parameterName} cannot be empty when supplied.");

        if (trimmed.Length > maxLength)
            throw new ValidationException($"{parameterName} cannot exceed {maxLength} characters.");

        return trimmed;
    }

    private static string? NormalizeSearchKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return null;

        var trimmed = TrimAndLimit(keywords, 120, nameof(keywords));
        if (trimmed.Contains('$', StringComparison.Ordinal) || trimmed.Contains(';', StringComparison.Ordinal))
            throw new ValidationException("keywords cannot contain OData or statement-control characters.");

        return trimmed;
    }

    private static bool TryParseDateTimeOffset(string? value, string parameterName, out DateTimeOffset? dateTimeOffset)
    {
        dateTimeOffset = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            throw new ValidationException($"{parameterName} must be a valid ISO 8601 date/time.");

        dateTimeOffset = parsed.ToUniversalTime();
        return true;
    }

    [Description("Confirm moving Outlook mail messages to a folder.")]
    public class GraphMailMoveConfirmationInput
    {
        [JsonPropertyName("mailbox")]
        [Description("Mailbox scope for the move operation.")]
        public string? Mailbox { get; set; }

        [JsonPropertyName("messageCount")]
        [Description("Number of messages that will be moved.")]
        public int MessageCount { get; set; }

        [JsonPropertyName("destinationFolderId")]
        [Description("Resolved destination folder ID.")]
        public string? DestinationFolderId { get; set; }

        [JsonPropertyName("destinationFolderDisplayName")]
        [Description("Resolved destination folder display name.")]
        public string? DestinationFolderDisplayName { get; set; }

        [JsonPropertyName("keywords")]
        public string? Keywords { get; set; }

        [JsonPropertyName("fromAddress")]
        public string? FromAddress { get; set; }

        [JsonPropertyName("subjectContains")]
        public string? SubjectContains { get; set; }

        [JsonPropertyName("receivedAfter")]
        public string? ReceivedAfter { get; set; }

        [JsonPropertyName("receivedBefore")]
        public string? ReceivedBefore { get; set; }

        [JsonPropertyName("unreadOnly")]
        public bool? UnreadOnly { get; set; }

        [JsonPropertyName("hasAttachments")]
        public bool? HasAttachments { get; set; }

        [JsonPropertyName("preview")]
        [Description("Preview of up to ten messages that will be moved.")]
        public string Preview { get; set; } = string.Empty;
    }

    public class GraphMailMoveQuery
    {
        [JsonPropertyName("keywords")]
        public string? Keywords { get; set; }

        [JsonPropertyName("search")]
        public string? Search { get; set; }

        [JsonPropertyName("filter")]
        public string? Filter { get; set; }

        [JsonPropertyName("top")]
        public int Top { get; set; }

        [JsonPropertyName("maxAllowed")]
        public int MaxAllowed { get; set; }

        [JsonPropertyName("clientSideKeywordFallback")]
        public bool ClientSideKeywordFallback { get; set; }
    }

    public class GraphMailMoveResult
    {
        [JsonPropertyName("mailbox")]
        public string? Mailbox { get; set; }

        [JsonPropertyName("destinationFolderId")]
        public string? DestinationFolderId { get; set; }

        [JsonPropertyName("destinationFolderDisplayName")]
        public string? DestinationFolderDisplayName { get; set; }

        [JsonPropertyName("requested")]
        public int Requested { get; set; }

        [JsonPropertyName("attempted")]
        public int Attempted { get; set; }

        [JsonPropertyName("moved")]
        public int Moved { get; set; }

        [JsonPropertyName("failed")]
        public int Failed { get; set; }

        [JsonPropertyName("query")]
        public GraphMailMoveQuery? Query { get; set; }

        [JsonPropertyName("messages")]
        public IReadOnlyList<GraphMailMoveItemResult> Messages { get; set; } = [];
    }

    public class GraphMailMoveItemResult
    {
        [JsonPropertyName("originalMessageId")]
        public string? OriginalMessageId { get; set; }

        [JsonPropertyName("movedMessageId")]
        public string? MovedMessageId { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("receivedDateTime")]
        public DateTimeOffset? ReceivedDateTime { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    [Description("Please fill in the draft e-mail details")]
    public class GraphCreateMailDraft
    {
        [JsonPropertyName("toRecipients")]
        [Required]
        [Description("E-mail addresses of the recipients. Use a comma separated list for multiple recipients.")]
        public string ToRecipients { get; set; } = string.Empty;

        [JsonPropertyName("ccRecipients")]
        [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")]
        public string? CcRecipients { get; set; }

        [JsonPropertyName("subject")]
        [Required]
        [Description("Subject of the draft e-mail message.")]
        public string? Subject { get; set; }

        [JsonPropertyName("body")]
        [Required]
        [Description("Body of the draft e-mail message.")]
        public string? Body { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Type of the message body (html or text).")]
        public BodyType? BodyType { get; set; }

        [JsonPropertyName("emailSignatureUrl")]
        [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the body.")]
        public string? EmailSignatureUrl { get; set; }
    }


    [Description("Please fill in the e-mail details")]
    public class GraphSendMail
    {
        [JsonPropertyName("toRecipients")]
        [Required]
        [Description("E-mail addresses of the recipients. Use a comma seperated list for multiple recipients.")]
        public string ToRecipients { get; set; } = string.Empty;

        [JsonPropertyName("ccRecipients")]
        [Description("E-mail addresses for CC (carbon copy). Use a comma separated list for multiple recipients.")]
        public string? CcRecipients { get; set; }

        [JsonPropertyName("subject")]
        [Required]
        [Description("Subject of the e-mail message.")]
        public string? Subject { get; set; }

        [JsonPropertyName("importance")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Importance.")]
        public Importance? Importance { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Type of the message body (html or text).")]
        public BodyType? BodyType { get; set; }

        [JsonPropertyName("body")]
        [Required]
        [Description("Body of the e-mail message.")]
        public string? Body { get; set; }

        [JsonPropertyName("emailSignatureUrl")]
        [Description("Optional URL to an HTML file containing the user's e-mail signature. Supports protected SharePoint/OneDrive links and will be appended to the body.")]
        public string? EmailSignatureUrl { get; set; }
    }
}
