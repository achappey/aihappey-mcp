using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static partial class GraphOutlookDelegatedMail
{
    [Description("Update an existing draft e-mail in a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Update delegated Outlook draft e-mail",
        Name = "graph_outlook_delegated_mail_update_draft",
        UseStructuredContent = true, OutputSchemaType = typeof(Message),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_UpdateDraft(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        [Description("Draft message ID.")][Required] string draftId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Replacement To recipients as comma-separated e-mail addresses. Leave empty to keep unchanged.")] string? toRecipients = null,
        [Description("Replacement CC recipients as comma-separated e-mail addresses. Leave empty to keep unchanged.")] string? ccRecipients = null,
        [Description("Replacement subject. Leave empty to keep unchanged.")] string? subject = null,
        [Description("Replacement message body. Leave empty to keep unchanged.")] string? body = null,
        [Description("Replacement body type. Leave empty to keep unchanged.")] BodyType? bodyType = null,
        [Description("Replacement importance. Leave empty to keep unchanged.")] Importance? importance = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
            if (toRecipients is null && ccRecipients is null && subject is null &&
                body is null && bodyType is null && importance is null)
                throw new ValidationException("At least one draft property must be provided.");

            var current = await client.Users[userId].Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found in mailbox '{userId}'.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (input, rejected, _) = await requestContext.TryElicit(
                new GraphOutlookMail.GraphUpdateMailDraft
                {
                    ToRecipients = toRecipients,
                    CcRecipients = ccRecipients,
                    Subject = subject,
                    Body = body,
                    BodyType = bodyType,
                    Importance = importance
                }, cancellationToken);

            if (input.ToRecipients is null && input.CcRecipients is null && input.Subject is null &&
                input.Body is null && input.BodyType is null && input.Importance is null)
                throw new ValidationException("At least one draft property must be provided.");

            return await client.Users[userId].Messages[draftId].PatchAsync(new Message
            {
                Subject = input.Subject,
                Importance = input.Importance,
                Body = input.Body is not null || input.BodyType is not null
                    ? new ItemBody { Content = input.Body, ContentType = input.BodyType ?? BodyType.Text }
                    : null,
                ToRecipients = input.ToRecipients is not null
                    ? [.. input.ToRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(value => value.ToRecipient())]
                    : null,
                CcRecipients = input.CcRecipients is not null
                    ? [.. input.CcRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(value => value.ToRecipient())]
                    : null
            }, cancellationToken: cancellationToken);
        })));

    [Description("Send an existing draft e-mail from a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Send delegated Outlook draft e-mail",
        Name = "graph_outlook_delegated_mail_send_draft", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_SendDraft(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        [Description("Draft message ID to send.")][Required] string draftId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent<object>(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
            var current = await client.Users[userId].Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found in mailbox '{userId}'.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (input, rejected, _) = await requestContext.TryElicit(
                new GraphDelegatedSendDraftInput { Mailbox = userId, DraftId = draftId, Subject = current.Subject },
                cancellationToken);

            if (input.Mailbox != userId || input.DraftId != draftId)
                throw new ValidationException("The confirmed mailbox and draft ID must match the requested draft.");

            await client.Users[userId].Messages[draftId].Send.PostAsync(cancellationToken: cancellationToken);
            return new { Mailbox = userId, DraftId = draftId, current.Subject, Status = "Sent" };
        })));

    [Description("Delete an existing draft e-mail from a delegated Outlook mailbox.")]
    [McpServerTool(Title = "Delete delegated Outlook draft e-mail",
        Name = "graph_outlook_delegated_mail_delete_draft", Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_DeleteDraft(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        [Description("Draft message ID to delete.")][Required] string draftId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
            var current = await client.Users[userId].Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found in mailbox '{userId}'.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            return await requestContext.ConfirmAndDeleteAsync<GraphOutlookMail.GraphDeleteMailDraft>(
                $"{userId}: {draftId}",
                async _ => await client.Users[userId].Messages[draftId].DeleteAsync(cancellationToken: cancellationToken),
                "Delegated Outlook draft e-mail deleted.", cancellationToken);
        }));

    [Description("Add a real file from a OneDrive or SharePoint URL to a delegated Outlook draft e-mail.")]
    [McpServerTool(Title = "Add file attachment to delegated Outlook draft",
        Name = "graph_outlook_delegated_mail_add_draft_attachment",
        UseStructuredContent = true, OutputSchemaType = typeof(FileAttachment),
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_AddDraftAttachment(
        IServiceProvider serviceProvider,
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        [Description("Draft message ID.")][Required] string draftId,
        [Description("Protected OneDrive or SharePoint URL of the file to attach.")][Required] string fileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional attachment filename override, including extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
            var current = await client.Users[userId].Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found in mailbox '{userId}'.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (input, rejected, _) = await requestContext.TryElicit(
                new GraphOutlookMail.GraphAddDraftAttachment { FileUrl = fileUrl, Filename = filename }, cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(input.FileUrl);

            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var downloaded = (await downloadService.DownloadContentAsync(
                serviceProvider, requestContext.Server, input.FileUrl, cancellationToken)).FirstOrDefault()
                ?? throw new ValidationException("The file could not be downloaded from the OneDrive or SharePoint URL.");
            var bytes = downloaded.Contents.ToArray();
            if (bytes.Length > 3 * 1024 * 1024)
                throw new ValidationException("The downloaded file exceeds the 3 MB direct Outlook attachment limit.");

            var attachment = new FileAttachment
            {
                OdataType = "#microsoft.graph.fileAttachment",
                Name = string.IsNullOrWhiteSpace(input.Filename)
                    ? downloaded.Filename ?? Path.GetFileName(new Uri(input.FileUrl).AbsolutePath)
                    : input.Filename,
                ContentType = string.IsNullOrWhiteSpace(downloaded.MimeType)
                    ? "application/octet-stream" : downloaded.MimeType,
                ContentBytes = bytes
            };
            var created = await client.Users[userId].Messages[draftId].Attachments.PostAsync(
                attachment, cancellationToken: cancellationToken);
            return created as FileAttachment;
        })));

    [Description("Delete an attachment from a delegated Outlook draft e-mail.")]
    [McpServerTool(Title = "Delete delegated Outlook draft attachment",
        Name = "graph_outlook_delegated_mail_delete_draft_attachment",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphDelegatedMail_DeleteDraftAttachment(
        [Description("Delegated user ID or mailbox address.")][Required] string userId,
        [Description("Draft message ID.")][Required] string draftId,
        [Description("Attachment ID to delete.")][Required] string attachmentId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
            ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
            var current = await client.Users[userId].Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found in mailbox '{userId}'.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            return await requestContext.ConfirmAndDeleteAsync<GraphOutlookMail.GraphDeleteDraftAttachment>(
                $"{userId}: {draftId} / {attachmentId}",
                async _ => await client.Users[userId].Messages[draftId].Attachments[attachmentId]
                    .DeleteAsync(cancellationToken: cancellationToken),
                "Delegated Outlook draft attachment deleted.", cancellationToken);
        }));

    [Description("Please confirm the delegated Outlook draft e-mail to send.")]
    public sealed class GraphDelegatedSendDraftInput
    {
        [Required]
        [JsonPropertyName("mailbox")]
        public string Mailbox { get; set; } = default!;

        [Required]
        [JsonPropertyName("draftId")]
        public string DraftId { get; set; } = default!;

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }
    }
}
