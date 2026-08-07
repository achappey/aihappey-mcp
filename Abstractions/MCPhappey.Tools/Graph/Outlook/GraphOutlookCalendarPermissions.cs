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

public static class GraphOutlookCalendarPermissions
{
    [Description("Create a sharing permission on one of the signed-in user's Outlook calendars.")]
    [McpServerTool(Title = "Create Outlook calendar permission", Name = "graph_outlook_calendar_permissions_create",
        UseStructuredContent = true, OutputSchemaType = typeof(CalendarPermission),
        Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendarPermissions_Create(
        [Description("Outlook calendar ID.")] string calendarId,
        [Description("Recipient e-mail address.")] string emailAddress,
        [Description("Permission role, such as Read, Write, DelegateWithoutPrivateEventAccess, or DelegateWithPrivateEventAccess.")] CalendarRoleType role,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional recipient display name.")] string? displayName = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new CalendarPermissionInput
                {
                    EmailAddress = emailAddress,
                    DisplayName = displayName,
                    Role = role
                }, cancellationToken);
            if (notAccepted is not null || input is null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));
            ArgumentException.ThrowIfNullOrWhiteSpace(input.EmailAddress);

            return await client.Me.Calendars[calendarId].CalendarPermissions.PostAsync(
                new CalendarPermission
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = input.EmailAddress.Trim(),
                        Name = input.DisplayName?.Trim()
                    },
                    Role = input.Role
                }, cancellationToken: cancellationToken);
        })));

    [Description("Change the role of an existing Outlook calendar permission.")]
    [McpServerTool(Title = "Update Outlook calendar permission", Name = "graph_outlook_calendar_permissions_update",
        UseStructuredContent = true, OutputSchemaType = typeof(CalendarPermission),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendarPermissions_Update(
        [Description("Outlook calendar ID.")] string calendarId,
        [Description("Calendar permission ID.")] string permissionId,
        [Description("Replacement permission role.")] CalendarRoleType role,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new CalendarPermissionRoleInput { Role = role }, cancellationToken);
            if (notAccepted is not null || input is null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            return await client.Me.Calendars[calendarId].CalendarPermissions[permissionId].PatchAsync(
                new CalendarPermission { Role = input.Role }, cancellationToken: cancellationToken);
        })));

    [Description("Delete an Outlook calendar sharing permission.")]
    [McpServerTool(Title = "Delete Outlook calendar permission", Name = "graph_outlook_calendar_permissions_delete",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphOutlookCalendarPermissions_Delete(
        [Description("Outlook calendar ID.")] string calendarId,
        [Description("Calendar permission ID to delete.")] string permissionId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteCalendarPermissionInput>(
            permissionId,
            async _ => await client.Me.Calendars[calendarId].CalendarPermissions[permissionId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Outlook calendar permission deleted.",
            cancellationToken)));

    [Description("Review the Outlook calendar permission.")]
    public sealed class CalendarPermissionInput
    {
        [Required, EmailAddress]
        [JsonPropertyName("emailAddress")]
        public string EmailAddress { get; set; } = default!;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [Required]
        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CalendarRoleType? Role { get; set; }
    }

    [Description("Review the replacement Outlook calendar permission role.")]
    public sealed class CalendarPermissionRoleInput
    {
        [Required]
        [JsonPropertyName("role")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CalendarRoleType? Role { get; set; }
    }

    [Description("Please confirm the Outlook calendar permission ID to delete: {0}")]
    public sealed class DeleteCalendarPermissionInput : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }
}
