using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Outlook;

public static class GraphOutlookMailFolders
{
    [Description("Create a top-level or child Outlook mail folder for the signed-in user.")]
    [McpServerTool(Title = "Create Outlook mail folder", Name = "graph_outlook_mail_folders_create",
        UseStructuredContent = true, OutputSchemaType = typeof(MailFolder), Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Create(
        [Description("Folder display name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional parent mail-folder ID. Omit to create a top-level folder.")] string? parentFolderId = null,
        [Description("Whether messages in the folder are hidden by default.")] bool isHidden = false,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new MailFolderInput { DisplayName = displayName, IsHidden = isHidden }, cancellationToken);
            ThrowIfRejected(input, rejected);
            ArgumentException.ThrowIfNullOrWhiteSpace(input!.DisplayName);
            var folder = new MailFolder { DisplayName = input.DisplayName.Trim(), IsHidden = input.IsHidden };
            return string.IsNullOrWhiteSpace(parentFolderId)
                ? await client.Me.MailFolders.PostAsync(folder, cancellationToken: cancellationToken)
                : await client.Me.MailFolders[parentFolderId].ChildFolders.PostAsync(folder, cancellationToken: cancellationToken);
        })));

    [Description("Rename an Outlook mail folder for the signed-in user.")]
    [McpServerTool(Title = "Update Outlook mail folder", Name = "graph_outlook_mail_folders_update",
        UseStructuredContent = true, OutputSchemaType = typeof(MailFolder),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Update(
        [Description("Mail-folder ID.")] string folderId,
        [Description("Replacement folder display name.")] string displayName,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new MailFolderPatchInput { DisplayName = displayName }, cancellationToken);
            ThrowIfRejected(input, rejected);
            ArgumentException.ThrowIfNullOrWhiteSpace(input!.DisplayName);
            return await client.Me.MailFolders[folderId].PatchAsync(
                new MailFolder { DisplayName = input.DisplayName.Trim() }, cancellationToken: cancellationToken);
        })));

    [Description("Delete an Outlook mail folder and its contained messages and child folders.")]
    [McpServerTool(Title = "Delete Outlook mail folder", Name = "graph_outlook_mail_folders_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> Delete(
        [Description("Mail-folder ID to delete.")] string folderId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteMailFolderInput>(folderId,
            async _ => await client.Me.MailFolders[folderId].DeleteAsync(cancellationToken: cancellationToken),
            "Outlook mail folder deleted.", cancellationToken)));

    private static void ThrowIfRejected(object? input, object? rejected)
    {
        if (input is null || rejected is not null) throw new Exception(JsonSerializer.Serialize(rejected));
    }

    [Description("Review the new Outlook mail folder.")]
    public sealed class MailFolderInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
        [JsonPropertyName("isHidden")] public bool IsHidden { get; set; }
    }

    [Description("Review the Outlook mail-folder changes.")]
    public sealed class MailFolderPatchInput
    {
        [Required, JsonPropertyName("displayName")] public string DisplayName { get; set; } = default!;
    }

    [Description("Please confirm the Outlook mail-folder ID to delete: {0}")]
    public sealed class DeleteMailFolderInput : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }
}
