using System.ComponentModel;
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
      [Description("The users's office location.")] string? officeLocation = null,
        [Description("The users's state.")] string? state = null,
        [Description("The users's country.")] string? country = null,
        [Description("The users's postal code.")] string? postalCode = null,
        [Description("The users's city.")] string? city = null,
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
                State = state,
                City = city,
                Country = country,
                OfficeLocation = officeLocation,
                PostalCode = postalCode,
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
            AccountEnabled = typed?.AccountEnabled,
            PasswordProfile = new PasswordProfile()
            {
                ForceChangePasswordNextSignIn = typed?.ForceChangePasswordNextSignIn,
                Password = typed?.Password
            },
            UserPrincipalName = typed?.UserPrincipalName
        };

        if (!string.IsNullOrEmpty(typed?.Country))
        {
            user.Country = typed.Country;
        }

        if (!string.IsNullOrEmpty(typed?.City))
        {
            user.City = typed.City;
        }

        if (!string.IsNullOrEmpty(typed?.State))
        {
            user.State = typed.State;
        }

        if (!string.IsNullOrEmpty(typed?.PostalCode))
        {
            user.PostalCode = typed.PostalCode;
        }

        if (!string.IsNullOrEmpty(typed?.OfficeLocation))
        {
            user.OfficeLocation = typed.OfficeLocation;
        }

        if (!string.IsNullOrEmpty(typed?.BusinessPhone))
        {
            user.BusinessPhones = [typed.BusinessPhone];
        }

        if (!string.IsNullOrEmpty(typed?.JobTitle))
        {
            user.JobTitle = typed.JobTitle;
        }

        if (!string.IsNullOrEmpty(typed?.CompanyName))
        {
            user.CompanyName = typed.CompanyName;
        }

        if (!string.IsNullOrEmpty(typed?.Department))
        {
            user.Department = typed.Department;
        }

        if (!string.IsNullOrEmpty(typed?.MobilePhone))
        {
            user.MobilePhone = typed.MobilePhone;
        }

        return await client.Users.PostAsync(user, cancellationToken: cancellationToken);
    })));

    [Description("Update a Microsoft 365 user")]
    [McpServerTool(Title = "Update a user",
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphUsers_UpdateUser(
        [Description("User id to update.")] string userId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The users's given name.")] string? givenName = null,
        [Description("The users's display name.")] string? displayName = null,
        [Description("The users's job title.")] string? jobTitle = null,
        [Description("The users's compay name.")] string? companyName = null,
        [Description("The users's department.")] string? department = null,
        [Description("The users's mobile phone.")] string? mobilePhone = null,
        [Description("The users's business phone.")] string? businessPhone = null,
        [Description("The users's office location.")] string? officeLocation = null,
        [Description("The users's state.")] string? state = null,
        [Description("The users's country.")] string? country = null,
        [Description("The users's postal code.")] string? postalCode = null,
        [Description("The users's city.")] string? city = null,
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
                Department = department ?? newUser?.Department ?? string.Empty,
                CompanyName = companyName ?? newUser?.CompanyName ?? string.Empty,
                State = state ?? newUser?.State ?? string.Empty,
                Country = country ?? newUser?.Country ?? string.Empty,
                City = city ?? newUser?.City ?? string.Empty,
                PostalCode = postalCode ?? newUser?.PostalCode ?? string.Empty,
                OfficeLocation = officeLocation ?? newUser?.OfficeLocation ?? string.Empty,
                MobilePhone = mobilePhone ?? newUser?.MobilePhone ?? string.Empty,
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
            AccountEnabled = typed?.AccountEnabled,
        };

        if (!string.IsNullOrEmpty(typed?.Country))
        {
            user.Country = typed.Country;
        }

        if (!string.IsNullOrEmpty(typed?.City))
        {
            user.City = typed.City;
        }

        if (!string.IsNullOrEmpty(typed?.State))
        {
            user.State = typed.State;
        }

        if (!string.IsNullOrEmpty(typed?.PostalCode))
        {
            user.PostalCode = typed.PostalCode;
        }

        if (!string.IsNullOrEmpty(typed?.OfficeLocation))
        {
            user.OfficeLocation = typed.OfficeLocation;
        }

        if (!string.IsNullOrEmpty(typed?.JobTitle))
        {
            user.JobTitle = typed.JobTitle;
        }

        if (!string.IsNullOrEmpty(typed?.BusinessPhone))
        {
            user.BusinessPhones = [typed.BusinessPhone];
        }

        if (!string.IsNullOrEmpty(typed?.CompanyName))
        {
            user.CompanyName = typed.CompanyName;
        }

        if (!string.IsNullOrEmpty(typed?.Department))
        {
            user.Department = typed.Department;
        }

        if (!string.IsNullOrEmpty(typed?.MobilePhone))
        {
            user.MobilePhone = typed.MobilePhone;
        }

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
