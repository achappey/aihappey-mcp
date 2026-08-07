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

public static partial class GraphOutlookCalendar
{
    [Description("Add a real file from a OneDrive or SharePoint URL to an Outlook calendar event.")]
    [McpServerTool(Title = "Add file attachment to Outlook calendar event",
        Name = "graph_outlook_calendar_add_event_attachment",
        UseStructuredContent = true,
        OutputSchemaType = typeof(FileAttachment),
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_AddEventAttachment(
        IServiceProvider serviceProvider,
        [Description("Calendar event ID.")] string eventId,
        [Description("Protected OneDrive or SharePoint URL of the file to attach.")] string fileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional attachment filename override, including extension.")] string? filename = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphAddEventAttachment { FileUrl = fileUrl, Filename = filename }, cancellationToken);
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

            var created = await client.Me.Events[eventId].Attachments.PostAsync(
                attachment, cancellationToken: cancellationToken);
            return created as FileAttachment;
        })));

    [Description("Delete an attachment from an Outlook calendar event.")]
    [McpServerTool(Title = "Delete Outlook calendar event attachment",
        Name = "graph_outlook_calendar_delete_event_attachment",
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendar_DeleteEventAttachment(
        [Description("Calendar event ID.")] string eventId,
        [Description("Attachment ID to delete.")] string attachmentId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteEventAttachment>(
            attachmentId,
            async _ => await client.Me.Events[eventId].Attachments[attachmentId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Outlook calendar event attachment deleted.",
            cancellationToken)));

    [Description("Please review the file attachment to add to the Outlook calendar event.")]
    public sealed class GraphAddEventAttachment
    {
        [Required]
        [JsonPropertyName("fileUrl")]
        [Description("Protected OneDrive or SharePoint URL of the file to attach.")]
        public string FileUrl { get; set; } = default!;

        [JsonPropertyName("filename")]
        [Description("Optional attachment filename override, including extension.")]
        public string? Filename { get; set; }
    }

    [Description("Please confirm the Outlook calendar event attachment ID to delete: {0}")]
    public sealed class GraphDeleteEventAttachment : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
