using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Users;

public static class GraphUserManagement
{
    [Description("Add a user to a group")]
    [McpServerTool(Title = "Add user to group", OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_AddUserToGroup(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The user id.")] string userId,
        [Description("The group id.")] string groupId,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
            new GraphAddUserToGroup()
            {
                UserId = userId ?? string.Empty,
                GroupId = groupId ?? string.Empty
            },
            cancellationToken
        );

        var refUser = new ReferenceCreate
        {
            OdataId = $"https://graph.microsoft.com/beta/users/{typed.UserId}"
        };

        await client.Groups[typed.GroupId].Members.Ref.PostAsync(refUser, cancellationToken: cancellationToken);

        return new
        {
            Message = $"User {typed.UserId} added to group {typed.GroupId}.",
            typed.UserId,
            typed.GroupId
        };
    })));

    [Description("Create a new user")]
    [McpServerTool(Title = "Create new user", OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_CreateUser(
      RequestContext<CallToolRequestParams> requestContext,
      [Description("The users's given name.")] string? givenName = null,
      [Description("The users's display name.")] string? displayName = null,
      [Description("The users's principal name.")] string? userPrincipalName = null,
      [Description("The users's mail nickname.")] string? mailNickname = null,
      [Description("The users's job title.")] string? jobTitle = null,
      [Description("The users's mobile phone.")] string? mobilePhone = null,
      [Description("The users's business phone.")] string? businessPhone = null,
      [Description("Account enabled.")] bool? accountEnabled = null,
      [Description("The users's department.")] string? department = null,
      [Description("The users's compay name.")] string? companyName = null,
      [Description("Force password change.")] bool? forceChangePasswordNextSignIn = null,
      [Description("The users's password.")] string? password = null,
      CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphNewUser
            {
                GivenName = givenName ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                UserPrincipalName = userPrincipalName ?? string.Empty,
                MailNickname = mailNickname ?? string.Empty,
                Department = department ?? string.Empty,
                MobilePhone = mobilePhone,
                BusinessPhone = businessPhone,
                CompanyName = companyName ?? string.Empty,
                AccountEnabled = accountEnabled ?? true,
                JobTitle = jobTitle ?? string.Empty,
                ForceChangePasswordNextSignIn = forceChangePasswordNextSignIn ?? true,
                Password = password ?? string.Empty
            },
            cancellationToken
        );

        var user = new User()
        {
            DisplayName = typed?.DisplayName,
            GivenName = typed?.GivenName,
            MailNickname = typed?.MailNickname,
            JobTitle = typed?.JobTitle,
            CompanyName = typed?.CompanyName,
            Department = typed?.Department,
            MobilePhone = typed?.MobilePhone,
            BusinessPhones = string.IsNullOrWhiteSpace(typed?.BusinessPhone) ? null : [typed.BusinessPhone],
            AccountEnabled = typed?.AccountEnabled,
            PasswordProfile = new PasswordProfile()
            {
                ForceChangePasswordNextSignIn = typed?.ForceChangePasswordNextSignIn,
                Password = typed?.Password
            },
            UserPrincipalName = typed?.UserPrincipalName
        };

        return await client.Users.PostAsync(user, cancellationToken: cancellationToken);
    })));

    [Description("Update a Microsoft 365 user")]
    [McpServerTool(Title = "Update a user",
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_UpdateUser(
        [Description("User id to update.")] string userId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The users's given name.")] string? givenName = null,
        [Description("The users's display name.")] string? displayName = null,
        [Description("The users's job title.")] string? jobTitle = null,
        [Description("The users's compay name.")] string? companyName = null,
        [Description("The users's department.")] string? department = null,
        [Description("The users's mobile phone.")] string? mobilePhone = null,
        [Description("The users's business phone.")] string? businessPhone = null,
        [Description("Account enabled.")] bool? accountEnabled = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
    {
        var newUser = await client.Users[userId].GetAsync(cancellationToken: cancellationToken);

        var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
            new GraphUpdateUser
            {
                GivenName = givenName ?? newUser?.GivenName ?? string.Empty,
                Department = department ?? newUser?.Department,
                CompanyName = companyName ?? newUser?.CompanyName,
                MobilePhone = mobilePhone ?? newUser?.MobilePhone,
                BusinessPhone = businessPhone ?? newUser?.BusinessPhones?.FirstOrDefault(),
                DisplayName = displayName ?? newUser?.DisplayName ?? string.Empty,
                AccountEnabled = accountEnabled ?? (newUser != null
                    && newUser.AccountEnabled.HasValue && newUser.AccountEnabled.Value),
                JobTitle = jobTitle ?? newUser?.JobTitle ?? string.Empty,
            },
            cancellationToken
        );
        var user = new User()
        {
            DisplayName = typed?.DisplayName,
            GivenName = typed?.GivenName,
            JobTitle = typed?.JobTitle,
            MobilePhone = typed?.MobilePhone,
            BusinessPhones = string.IsNullOrWhiteSpace(typed?.BusinessPhone) ? null : [typed.BusinessPhone],
            Department = typed?.Department,
            CompanyName = typed?.CompanyName,
            AccountEnabled = typed?.AccountEnabled,
        };

        return await client.Users[userId].PatchAsync(user, cancellationToken: cancellationToken);
    })));

    [Description("Delete an user.")]
    [McpServerTool(Title = "Delete user",
        OpenWorld = false,
        Destructive = true,
        ReadOnly = false)]
    public static async Task<CallToolResult?> GraphUsers_DeleteUser(
    [Description("User id.")]
        string? userId,
    RequestContext<CallToolRequestParams> requestContext,
    CancellationToken cancellationToken = default) =>
    await requestContext.WithExceptionCheck(async () =>
    await requestContext.WithOboGraphClient(async client =>
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Provide either entraDeviceId or managedDeviceId.");

        // Fetch Entra device display name for human confirmation
        var device = await client.Users[userId!]
            .GetAsync(rq =>
            {
                rq.QueryParameters.Select = ["id", "displayName"];
            }, cancellationToken);

        if (device is null)
            throw new InvalidOperationException($"User '{userId}' not found.");

        var display = string.IsNullOrWhiteSpace(device.DisplayName) ? device.Id : device.DisplayName;

        return await requestContext.ConfirmAndDeleteAsync<GraphDeleteUser>(
            expectedName: display!,
            deleteAction: async _ =>
            {
                // DELETE /devices/{id}
                await client.Users[device!.Id!].DeleteAsync(cancellationToken: cancellationToken);
            },
            successText: $"User '{display}' ({device.Id}) deleted.",
            ct: cancellationToken);
    }));

}
