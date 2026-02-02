using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookMail
{
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


    [Description("Search for e-mails in Outlook using Microsoft Graph. Supports subject, body, sender, and date filters.")]
    [McpServerTool(Title = "Search e-mails in Outlook",
        Name = "graph_outlook_mail_search",
        OpenWorld = true, Destructive = false, ReadOnly = true)]
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
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphSendMail
            {
                ToRecipients = toRecipients ?? string.Empty,
                CcRecipients = ccRecipients,
                Subject = subject,
                Body = body,
                BodyType = bodyType ?? BodyType.Text
            },
            cancellationToken
        );

        if (notAccepted != null) throw new Exception(JsonSerializer.Serialize(notAccepted));

        Message newMessage = new()
        {
            Subject = typed?.Subject,
            Body = new ItemBody
            {
                ContentType = typed?.BodyType,
                Content = typed?.Body
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
                BodyType = bodyType ?? BodyType.Text
            },
            cancellationToken
        );

        if (notAccepted != null) return notAccepted;

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

        var createdMessage = await client.Me.Messages.PostAsync(newMessage, cancellationToken: cancellationToken);
        return createdMessage.ToJsonContentBlock($"https://graph.microsoft.com/beta/me/messages/{createdMessage?.Id}").ToCallToolResult();
    }));

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

        [JsonPropertyName("body")]
        [Required]
        [Description("Body of the e-mail message.")]
        public string? Body { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [Description("Type of the message body (html or text).")]
        public BodyType? BodyType { get; set; }

    }
}