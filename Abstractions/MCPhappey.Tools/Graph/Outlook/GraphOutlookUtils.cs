using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookUtils
{
    private const string MessageRfc822MimeType = "message/rfc822";
    private const string OctetStreamMimeType = "application/octet-stream";
    private const int GraphAttachmentPageSize = 999;

    [Description("Save an Outlook e-mail as an .eml file and save its file attachments as separate files into a SharePoint or OneDrive folder. Supports the current user's mailbox, or a delegated/shared mailbox when userId is provided.")]
    [McpServerTool(
        Title = "Save Outlook e-mail and attachments to SharePoint/OneDrive folder",
        Name = "graph_outlook_save_message_to_sharepoint_folder",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlook_SaveMessageToSharePointFolder(
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        [Description("The Outlook message ID to save.")][Required] string messageId,
        [Description("Destination SharePoint or OneDrive folder URL where the .eml file and attachment files should be uploaded.")][Required] string destinationFolderUrl,
        [Description("Optional delegated/shared mailbox user ID or SMTP address. Leave empty to use the current user's mailbox.")] string? userId = null,
        [Description("Optional output base filename for the saved e-mail. '.eml' is appended when omitted. Attachment files keep their original names.")] string? filename = null,
        [Description("When true, only attachment files are saved and the .eml message file is skipped.")] bool saveAttachmentsOnly = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolderUrl);

        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        using var graphHttpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);

        var mailboxPath = BuildMailboxPath(userId);
        var mailbox = string.IsNullOrWhiteSpace(userId) ? "me" : userId.Trim();
        var message = await GetMessageMetadataAsync(graphClient, messageId, userId, cancellationToken)
            ?? throw new ValidationException($"Message '{messageId}' was not found in mailbox '{mailbox}'.");

        var baseName = BuildMessageBaseFileName(message, filename);
        var uploadedLinks = new List<ResourceLinkBlock>();
        var savedMail = default(SavedOutlookFileResult);
        var savedAttachments = new List<SavedOutlookFileResult>();
        var skippedAttachments = new List<SkippedOutlookAttachmentResult>();

        if (!saveAttachmentsOnly)
        {
            var emlFileName = EnsureExtension(baseName, ".eml");
            var mimeBytes = await DownloadMessageMimeAsync(graphHttpClient, mailboxPath, messageId, cancellationToken);
            var uploadedMail = await graphClient.UploadToFolder(
                destinationFolderUrl,
                emlFileName,
                BinaryData.FromBytes(mimeBytes),
                cancellationToken);

            if (uploadedMail != null)
            {
                uploadedLinks.Add(uploadedMail);
                savedMail = new SavedOutlookFileResult
                {
                    Name = uploadedMail.Name,
                    Uri = uploadedMail.Uri,
                    MimeType = uploadedMail.MimeType ?? MessageRfc822MimeType,
                    Size = uploadedMail.Size,
                    Kind = "mail"
                };
            }
        }

        var attachmentFiles = await GetFileAttachmentsAsync(graphHttpClient, mailboxPath, messageId, skippedAttachments, cancellationToken);
        var usedAttachmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in attachmentFiles)
        {
            var uploadName = MakeUniqueAttachmentName(SanitizeFileName(attachment.Name), usedAttachmentNames);
            var uploadedAttachment = await graphClient.UploadToFolder(
                destinationFolderUrl,
                uploadName,
                BinaryData.FromBytes(attachment.Content),
                cancellationToken);

            if (uploadedAttachment == null)
                continue;

            uploadedLinks.Add(uploadedAttachment);
            savedAttachments.Add(new SavedOutlookFileResult
            {
                Name = uploadedAttachment.Name,
                Uri = uploadedAttachment.Uri,
                MimeType = uploadedAttachment.MimeType ?? attachment.ContentType ?? OctetStreamMimeType,
                Size = uploadedAttachment.Size,
                Kind = attachment.IsInline ? "inline-file-attachment" : "file-attachment",
                SourceAttachmentId = attachment.Id,
                SourceAttachmentName = attachment.Name
            });
        }

        var result = new OutlookSaveMessageResult
        {
            Mailbox = mailbox,
            MessageId = messageId,
            DestinationFolderUrl = destinationFolderUrl,
            SaveAttachmentsOnly = saveAttachmentsOnly,
            Subject = message.Subject,
            SavedMail = savedMail,
            SavedAttachments = savedAttachments,
            SkippedAttachments = skippedAttachments,
            UploadedFileCount = (savedMail == null ? 0 : 1) + savedAttachments.Count
        };

        return new CallToolResult
        {
            Content = uploadedLinks.Count == 0
                ? ["No mail file or file attachments were uploaded.".ToTextContentBlock()]
                : [.. uploadedLinks],
            StructuredContent = JsonSerializer.SerializeToElement(result, JsonSerializerOptions.Web)
        };
    });

    private static async Task<Message?> GetMessageMetadataAsync(
        GraphServiceClient graphClient,
        string messageId,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return await graphClient.Me.Messages[messageId].GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = [
                    "id",
                    "subject",
                    "from",
                    "receivedDateTime",
                    "sentDateTime",
                    "hasAttachments"
                ];
            }, cancellationToken);
        }

        return await graphClient.Users[userId.Trim()].Messages[messageId].GetAsync(requestConfiguration =>
        {
            requestConfiguration.QueryParameters.Select = [
                "id",
                "subject",
                "from",
                "receivedDateTime",
                "sentDateTime",
                "hasAttachments"
            ];
        }, cancellationToken);
    }

    private static async Task<byte[]> DownloadMessageMimeAsync(
        HttpClient graphHttpClient,
        string mailboxPath,
        string messageId,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{mailboxPath}/messages/{EscapeSegment(messageId)}/$value";
        using var response = await graphHttpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to download Outlook message MIME content. {(int)response.StatusCode} {response.StatusCode}: {error}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<OutlookAttachmentPayload>> GetFileAttachmentsAsync(
        HttpClient graphHttpClient,
        string mailboxPath,
        string messageId,
        ICollection<SkippedOutlookAttachmentResult> skippedAttachments,
        CancellationToken cancellationToken)
    {
        var attachments = new List<OutlookAttachmentPayload>();
        string? requestUri = $"{mailboxPath}/messages/{EscapeSegment(messageId)}/attachments?$top={GraphAttachmentPageSize}&$select=id,name,contentType,size,isInline";

        while (!string.IsNullOrWhiteSpace(requestUri))
        {
            using var response = await graphHttpClient.GetAsync(requestUri, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Failed to list Outlook message attachments. {(int)response.StatusCode} {response.StatusCode}: {json}");

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    await TryAddAttachmentAsync(graphHttpClient, mailboxPath, messageId, item, attachments, skippedAttachments, cancellationToken);
                }
            }

            requestUri = document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
                ? nextLink.GetString()
                : null;
        }

        return attachments;
    }

    private static async Task TryAddAttachmentAsync(
        HttpClient graphHttpClient,
        string mailboxPath,
        string messageId,
        JsonElement item,
        ICollection<OutlookAttachmentPayload> attachments,
        ICollection<SkippedOutlookAttachmentResult> skippedAttachments,
        CancellationToken cancellationToken)
    {
        var odataType = GetString(item, "@odata.type") ?? string.Empty;
        var id = GetString(item, "id");
        var name = GetString(item, "name") ?? "attachment.bin";

        if (!odataType.EndsWith("fileAttachment", StringComparison.OrdinalIgnoreCase))
        {
            skippedAttachments.Add(new SkippedOutlookAttachmentResult
            {
                Id = id,
                Name = name,
                ODataType = odataType,
                Reason = "Only Microsoft Graph fileAttachment payloads are currently saved as separate files."
            });
            return;
        }

        byte[]? content = null;
        if (!string.IsNullOrWhiteSpace(id))
            content = await DownloadAttachmentValueAsync(graphHttpClient, mailboxPath, messageId, id, cancellationToken);

        if (content == null || content.Length == 0)
        {
            skippedAttachments.Add(new SkippedOutlookAttachmentResult
            {
                Id = id,
                Name = name,
                ODataType = odataType,
                Reason = "The file attachment did not include downloadable content."
            });
            return;
        }

        attachments.Add(new OutlookAttachmentPayload
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? "attachment.bin" : name,
            ContentType = GetString(item, "contentType"),
            IsInline = GetBoolean(item, "isInline"),
            Content = content
        });
    }

    private static async Task<byte[]?> DownloadAttachmentValueAsync(
        HttpClient graphHttpClient,
        string mailboxPath,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken)
    {
        var requestUri = $"{mailboxPath}/messages/{EscapeSegment(messageId)}/attachments/{EscapeSegment(attachmentId)}/$value";
        using var response = await graphHttpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Failed to download Outlook attachment '{attachmentId}'. {(int)response.StatusCode} {response.StatusCode}: {error}");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static string BuildMailboxPath(string? userId)
        => string.IsNullOrWhiteSpace(userId)
            ? "me"
            : $"users/{EscapeSegment(userId.Trim())}";

    private static string BuildMessageBaseFileName(Message message, string? filename)
    {
        if (!string.IsNullOrWhiteSpace(filename))
            return StripExtension(SanitizeFileName(filename.Trim()), ".eml");

        var timestamp = message.ReceivedDateTime ?? message.SentDateTime ?? DateTimeOffset.UtcNow;
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? "Outlook message" : message.Subject;
        return SanitizeFileName($"{timestamp:yyyyMMdd_HHmmss}_{subject}");
    }

    private static string MakeUniqueAttachmentName(string fileName, ISet<string> usedNames)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : fileName;
        if (usedNames.Add(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var i = 2; ; i++)
        {
            var next = $"{stem}_{i}{extension}";
            if (usedNames.Add(next))
                return next;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        sanitized = string.Join(" ", sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(sanitized) ? "outlook-message" : sanitized.Length <= 180 ? sanitized : sanitized[..180].Trim();
    }

    private static string EnsureExtension(string fileName, string extension)
        => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + extension;

    private static string StripExtension(string fileName, string extension)
        => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? fileName[..^extension.Length] : fileName;

    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(value);

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private sealed class OutlookAttachmentPayload
    {
        public string? Id { get; init; }
        public string Name { get; init; } = "attachment.bin";
        public string? ContentType { get; init; }
        public bool IsInline { get; init; }
        public byte[] Content { get; init; } = [];
    }

    private sealed class OutlookSaveMessageResult
    {
        public string Mailbox { get; init; } = default!;
        public string MessageId { get; init; } = default!;
        public string DestinationFolderUrl { get; init; } = default!;
        public bool SaveAttachmentsOnly { get; init; }
        public string? Subject { get; init; }
        public SavedOutlookFileResult? SavedMail { get; init; }
        public IReadOnlyList<SavedOutlookFileResult> SavedAttachments { get; init; } = [];
        public IReadOnlyList<SkippedOutlookAttachmentResult> SkippedAttachments { get; init; } = [];
        public int UploadedFileCount { get; init; }
    }

    private sealed class SavedOutlookFileResult
    {
        public string? Name { get; init; }
        public string? Uri { get; init; }
        public string? MimeType { get; init; }
        public long? Size { get; init; }
        public string Kind { get; init; } = default!;
        public string? SourceAttachmentId { get; init; }
        public string? SourceAttachmentName { get; init; }
    }

    private sealed class SkippedOutlookAttachmentResult
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? ODataType { get; init; }
        public string Reason { get; init; } = default!;
    }
}
