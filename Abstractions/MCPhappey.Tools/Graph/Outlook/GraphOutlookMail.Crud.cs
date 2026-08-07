using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static partial class GraphOutlookMail
{
    [Description("Create a mail folder in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Create Outlook mail folder",
        Name = "graph_outlook_mail_create_folder",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MailFolder),
        Destructive = false,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_CreateFolder(
        [Description("Display name of the new mail folder.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional parent mail folder ID. Leave empty to create a top-level folder.")] string? parentFolderId = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphMailFolderInput
                {
                    DisplayName = displayName,
                    ParentFolderId = parentFolderId
                }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            ArgumentException.ThrowIfNullOrWhiteSpace(typed?.DisplayName);
            var folder = new MailFolder { DisplayName = typed.DisplayName.Trim() };

            return string.IsNullOrWhiteSpace(typed.ParentFolderId)
                ? await client.Me.MailFolders.PostAsync(folder, cancellationToken: cancellationToken)
                : await client.Me.MailFolders[typed.ParentFolderId].ChildFolders
                    .PostAsync(folder, cancellationToken: cancellationToken);
        })));

    [Description("Rename a mail folder in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Rename Outlook mail folder",
        Name = "graph_outlook_mail_rename_folder",
        UseStructuredContent = true,
        OutputSchemaType = typeof(MailFolder),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_RenameFolder(
        [Description("Mail folder ID to rename.")] string folderId,
        [Description("New display name for the mail folder.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphMailFolderInput { DisplayName = displayName }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            ArgumentException.ThrowIfNullOrWhiteSpace(typed?.DisplayName);
            return await client.Me.MailFolders[folderId].PatchAsync(
                new MailFolder { DisplayName = typed.DisplayName.Trim() },
                cancellationToken: cancellationToken);
        })));

    [Description("Delete a mail folder from the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Delete Outlook mail folder",
        Name = "graph_outlook_mail_delete_folder",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_DeleteFolder(
        [Description("Mail folder ID to delete.")] string folderId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteMailFolder>(
            folderId,
            async _ => await client.Me.MailFolders[folderId].DeleteAsync(cancellationToken: cancellationToken),
            "Outlook mail folder deleted.",
            cancellationToken)));

    [Description("Update an existing draft e-mail in the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Update Outlook draft e-mail",
        Name = "graph_outlook_mail_update_draft",
        UseStructuredContent = true,
        OutputSchemaType = typeof(Message),
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_UpdateDraft(
        [Description("Draft message ID.")] string draftId,
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
            if (toRecipients is null && ccRecipients is null && subject is null &&
                body is null && bodyType is null && importance is null)
                throw new ValidationException("At least one draft property must be provided.");

            var current = await client.Me.Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found.");

            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateMailDraft
                {
                    ToRecipients = toRecipients,
                    CcRecipients = ccRecipients,
                    Subject = subject,
                    Body = body,
                    BodyType = bodyType,
                    Importance = importance
                }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            var update = new Message
            {
                Subject = typed?.Subject,
                Importance = typed?.Importance,
                Body = typed?.Body is not null || typed?.BodyType is not null
                    ? new ItemBody { Content = typed.Body, ContentType = typed.BodyType ?? BodyType.Text }
                    : null,
                ToRecipients = typed?.ToRecipients is not null
                    ? [.. typed.ToRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => value.ToRecipient())]
                    : null,
                CcRecipients = typed?.CcRecipients is not null
                    ? [.. typed.CcRecipients.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => value.ToRecipient())]
                    : null
            };

            return await client.Me.Messages[draftId].PatchAsync(update, cancellationToken: cancellationToken);
        })));

    [Description("Delete an existing draft e-mail from the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Delete Outlook draft e-mail",
        Name = "graph_outlook_mail_delete_draft",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_DeleteDraft(
        [Description("Draft message ID to delete.")] string draftId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var current = await client.Me.Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found.");

            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            return await requestContext.ConfirmAndDeleteAsync<GraphDeleteMailDraft>(
                draftId,
                async _ => await client.Me.Messages[draftId].DeleteAsync(cancellationToken: cancellationToken),
                "Outlook draft e-mail deleted.",
                cancellationToken);
        }));

    [Description("Send an existing draft e-mail from the signed-in user's Outlook mailbox.")]
    [McpServerTool(Title = "Send Outlook draft e-mail",
        Name = "graph_outlook_mail_send_draft",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_SendDraft(
        [Description("Draft message ID to send.")] string draftId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var current = await client.Me.Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "subject", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found.");

            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphSendMailDraft
                {
                    DraftId = draftId,
                    Subject = current.Subject
                }, cancellationToken);

            if (notAccepted is not null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));
            if (typed?.DraftId != draftId)
                throw new ValidationException("The confirmed draft ID does not match the requested draft ID.");

            await client.Me.Messages[draftId].Send.PostAsync(cancellationToken: cancellationToken);
            return new { DraftId = draftId, current.Subject, Status = "Sent" };
        })));

    [Description("Add a real file from a OneDrive or SharePoint URL to an Outlook draft e-mail.")]
    [McpServerTool(Title = "Add file attachment to Outlook draft",
        Name = "graph_outlook_mail_add_draft_attachment",
        UseStructuredContent = true, OutputSchemaType = typeof(FileAttachment),
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_AddDraftAttachment(
        IServiceProvider serviceProvider,
        [Description("Draft message ID.")] string draftId,
        [Description("Protected OneDrive or SharePoint URL of the file to attach.")] string fileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional attachment filename override, including extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var current = await client.Me.Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphAddDraftAttachment
                {
                    FileUrl = fileUrl,
                    Filename = filename
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(FileAttachment);
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
                    ? "application/octet-stream"
                    : downloaded.MimeType,
                ContentBytes = bytes
            };

            var created = await client.Me.Messages[draftId].Attachments.PostAsync(
                attachment, cancellationToken: cancellationToken);
            return created as FileAttachment;
        })));

    [Description("Delete an attachment from an Outlook draft e-mail.")]
    [McpServerTool(Title = "Delete Outlook draft attachment",
        Name = "graph_outlook_mail_delete_draft_attachment",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookMail_DeleteDraftAttachment(
        [Description("Draft message ID.")] string draftId,
        [Description("Attachment ID to delete.")] string attachmentId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var current = await client.Me.Messages[draftId].GetAsync(config =>
            {
                config.QueryParameters.Select = ["id", "isDraft"];
            }, cancellationToken) ?? throw new ValidationException($"Draft '{draftId}' was not found.");
            if (current.IsDraft != true)
                throw new ValidationException($"Message '{draftId}' is not a draft.");

            return await requestContext.ConfirmAndDeleteAsync<GraphDeleteDraftAttachment>(
                attachmentId,
                async _ => await client.Me.Messages[draftId].Attachments[attachmentId]
                    .DeleteAsync(cancellationToken: cancellationToken),
                "Outlook draft attachment deleted.", cancellationToken);
        }));

    [Description("Please fill in the Outlook mail folder details.")]
    public sealed class GraphMailFolderInput
    {
        [Required]
        [JsonPropertyName("displayName")]
        [Description("Display name of the mail folder.")]
        public string DisplayName { get; set; } = default!;

        [JsonPropertyName("parentFolderId")]
        [Description("Optional parent mail folder ID.")]
        public string? ParentFolderId { get; set; }
    }

    [Description("Please fill in the Outlook draft fields to update.")]
    public sealed class GraphUpdateMailDraft
    {
        [JsonPropertyName("toRecipients")]
        public string? ToRecipients { get; set; }

        [JsonPropertyName("ccRecipients")]
        public string? CcRecipients { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("bodyType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BodyType? BodyType { get; set; }

        [JsonPropertyName("importance")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Importance? Importance { get; set; }
    }

    [Description("Please confirm the Outlook mail folder ID to delete: {0}")]
    public sealed class GraphDeleteMailFolder : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the Outlook draft ID to delete: {0}")]
    public sealed class GraphDeleteMailDraft : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the draft e-mail to send.")]
    public sealed class GraphSendMailDraft
    {
        [Required]
        [JsonPropertyName("draftId")]
        public string DraftId { get; set; } = default!;

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }
    }

    [Description("Please review the file attachment to add to the Outlook draft.")]
    public sealed class GraphAddDraftAttachment
    {
        [Required]
        [JsonPropertyName("fileUrl")]
        [Description("Protected OneDrive or SharePoint URL of the file to attach.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Description("Optional attachment filename override, including extension.")]
        public string? Filename { get; set; }
    }

    [Description("Please confirm the Outlook draft attachment ID to delete: {0}")]
    public sealed class GraphDeleteDraftAttachment : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
