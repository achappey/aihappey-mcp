using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Drives.Item.Items.Item.CreateLink;
using Microsoft.Graph.Beta.Drives.Item.Items.Item.Invite;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.OneDrive;

public static class GraphOneDriveSharing
{
    [Description("Create a sharing link for a OneDrive or SharePoint drive item.")]
    [McpServerTool(Title = "Create drive item sharing link", Name = "graph_onedrive_sharing_create_link",
        UseStructuredContent = true, OutputSchemaType = typeof(Permission), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDriveSharing_CreateLink(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID to share.")] string itemId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Link role: view, edit, or embed.")] string type = "view",
        [Description("Link scope: anonymous, organization, users, or existingAccess.")] string scope = "organization",
        [Description("Optional UTC expiration date and time.")] DateTimeOffset? expirationDateTime = null,
        [Description("Optional password, where supported by tenant policy.")] string? password = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreateSharingLinkInput
                {
                    Type = type,
                    Scope = scope,
                    ExpirationDateTime = expirationDateTime,
                    Password = password
                }, cancellationToken);

            if (notAccepted is not null)
                return default(Permission);

            ValidateLink(typed);
            return await client.Drives[driveId].Items[itemId].CreateLink.PostAsync(
                new CreateLinkPostRequestBody
                {
                    Type = typed!.Type.Trim().ToLowerInvariant(),
                    Scope = typed.Scope.Trim().ToLowerInvariant(),
                    ExpirationDateTime = typed.ExpirationDateTime,
                    Password = NullIfWhiteSpace(typed.Password)
                }, cancellationToken: cancellationToken);
        })));

    [Description("Grant specific recipients access to a OneDrive or SharePoint drive item.")]
    [McpServerTool(Title = "Invite recipients to drive item", Name = "graph_onedrive_sharing_invite",
        UseStructuredContent = true, OutputSchemaType = typeof(PermissionCollectionResponse),
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDriveSharing_Invite(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID to share.")] string itemId,
        [Description("Comma-separated recipient email addresses.")] string recipientEmailsCsv,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Permission role: read or write.")] string role = "read",
        [Description("Whether Graph sends an invitation email.")] bool sendInvitation = true,
        [Description("Optional invitation message.")] string? message = null,
        [Description("Whether recipients must sign in.")] bool requireSignIn = true,
        [Description("Optional UTC permission expiration date and time.")] DateTimeOffset? expirationDateTime = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new InviteRecipientsInput
                {
                    RecipientEmailsCsv = recipientEmailsCsv,
                    Role = role,
                    SendInvitation = sendInvitation,
                    Message = message,
                    RequireSignIn = requireSignIn,
                    ExpirationDateTime = expirationDateTime
                }, cancellationToken);

            if (notAccepted is not null)
                return default(PermissionCollectionResponse);

            ValidateInvitation(typed);
            var recipients = typed!.RecipientEmailsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(email => new DriveRecipient { Email = email }).ToList();

            var response = await client.Drives[driveId].Items[itemId].Invite.PostAsInvitePostResponseAsync(
                new InvitePostRequestBody
                {
                    Recipients = recipients,
                    Roles = [typed.Role.Trim().ToLowerInvariant()],
                    SendInvitation = typed.SendInvitation,
                    Message = NullIfWhiteSpace(typed.Message),
                    RequireSignIn = typed.RequireSignIn,
                    ExpirationDateTime = typed.ExpirationDateTime?.ToString("O")
                }, cancellationToken: cancellationToken);

            return new PermissionCollectionResponse { Value = response?.Value };
        })));

    [Description("Change the role on an existing direct drive-item permission.")]
    [McpServerTool(Title = "Update drive item permission", Name = "graph_onedrive_sharing_update_permission",
        UseStructuredContent = true, OutputSchemaType = typeof(Permission), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDriveSharing_UpdatePermission(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID.")] string itemId,
        [Description("Permission ID to update.")] string permissionId,
        [Description("New permission role: read or write.")] string role,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new UpdatePermissionInput { PermissionId = permissionId, Role = role }, cancellationToken);
            if (notAccepted is not null)
                return default(Permission);

            ValidateRole(typed?.Role);
            return await client.Drives[driveId].Items[itemId].Permissions[typed!.PermissionId].PatchAsync(
                new Permission { Roles = [typed.Role.Trim().ToLowerInvariant()] },
                cancellationToken: cancellationToken);
        })));

    [Description("Revoke an existing sharing permission from a OneDrive or SharePoint drive item.")]
    [McpServerTool(Title = "Revoke drive item permission", Name = "graph_onedrive_sharing_revoke_permission",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOneDriveSharing_RevokePermission(
        [Description("Drive ID containing the item.")] string driveId,
        [Description("Drive item ID.")] string itemId,
        [Description("Permission ID to revoke.")] string permissionId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<RevokePermissionInput>(
            permissionId,
            async _ => await client.Drives[driveId].Items[itemId].Permissions[permissionId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Drive item permission revoked.", cancellationToken)));

    [Description("Please review the sharing link settings.")]
    public sealed class CreateSharingLinkInput
    {
        [Required, JsonPropertyName("type")]
        [Description("Link role: view, edit, or embed.")]
        public string Type { get; set; } = "view";

        [Required, JsonPropertyName("scope")]
        [Description("Link scope: anonymous, organization, users, or existingAccess.")]
        public string Scope { get; set; } = "organization";

        [JsonPropertyName("expirationDateTime")]
        public DateTimeOffset? ExpirationDateTime { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }
    }

    [Description("Please review the recipient invitation settings.")]
    public sealed class InviteRecipientsInput
    {
        [Required, JsonPropertyName("recipientEmailsCsv")]
        public string RecipientEmailsCsv { get; set; } = default!;

        [Required, JsonPropertyName("role")]
        public string Role { get; set; } = "read";

        [JsonPropertyName("sendInvitation")]
        public bool SendInvitation { get; set; } = true;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("requireSignIn")]
        public bool RequireSignIn { get; set; } = true;

        [JsonPropertyName("expirationDateTime")]
        public DateTimeOffset? ExpirationDateTime { get; set; }
    }

    [Description("Please review the permission role change.")]
    public sealed class UpdatePermissionInput
    {
        [Required, JsonPropertyName("permissionId")]
        public string PermissionId { get; set; } = default!;

        [Required, JsonPropertyName("role")]
        public string Role { get; set; } = default!;
    }

    [Description("Please confirm the drive item permission ID to revoke: {0}")]
    public sealed class RevokePermissionInput : MCPhappey.Common.Models.IHasName
    {
        [Required, JsonPropertyName("name")]
        public string Name { get; set; } = default!;
    }

    private static void ValidateLink(CreateSharingLinkInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!new[] { "view", "edit", "embed" }.Contains(input.Type, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("Link type must be view, edit, or embed.");
        if (!new[] { "anonymous", "organization", "users", "existingAccess" }.Contains(input.Scope, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("Link scope must be anonymous, organization, users, or existingAccess.");
    }

    private static void ValidateInvitation(InviteRecipientsInput? input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateRole(input.Role);
        var emails = input.RecipientEmailsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (emails.Length == 0 || emails.Any(email => !new EmailAddressAttribute().IsValid(email)))
            throw new ValidationException("Provide one or more valid comma-separated recipient email addresses.");
    }

    private static void ValidateRole(string? role)
    {
        if (!new[] { "read", "write" }.Contains(role, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("Permission role must be read or write.");
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
