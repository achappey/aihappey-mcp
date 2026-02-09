using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Security;

public static class GraphAuthenticationMethodsAdmin
{
    [Description("Add an email authentication method for a user.")]
    [McpServerTool(Title = "Add user email auth method", Name = "graph_auth_admin_add_email_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_AddEmailMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Email address to register.")] string emailAddress,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new AddEmailInput
            {
                UserId = userId,
                EmailAddress = emailAddress
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/emailMethods";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, new
            {
                emailAddress = typed.EmailAddress
            }, cancellationToken);
        }));

    [Description("Add a phone authentication method for a user.")]
    [McpServerTool(Title = "Add user phone auth method", Name = "graph_auth_admin_add_phone_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_AddPhoneMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Phone number in E.164 format.")] string phoneNumber,
        [Description("Phone type: mobile, alternateMobile, office.")] string phoneType,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new AddPhoneInput
            {
                UserId = userId,
                PhoneNumber = phoneNumber,
                PhoneType = phoneType
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/phoneMethods";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, new
            {
                phoneNumber = typed.PhoneNumber,
                phoneType = typed.PhoneType
            }, cancellationToken);
        }));

    [Description("Update a user's phone authentication method.")]
    [McpServerTool(Title = "Update user phone auth method", Name = "graph_auth_admin_update_phone_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_UpdatePhoneMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Phone method id.")] string phoneMethodId,
        [Description("Phone number in E.164 format.")] string phoneNumber,
        [Description("Phone type: mobile, alternateMobile, office.")] string phoneType,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new UpdatePhoneInput
            {
                UserId = userId,
                PhoneMethodId = phoneMethodId,
                PhoneNumber = phoneNumber,
                PhoneType = phoneType
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/phoneMethods/{Uri.EscapeDataString(typed.PhoneMethodId)}";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Patch, path, new
            {
                phoneNumber = typed.PhoneNumber,
                phoneType = typed.PhoneType
            }, cancellationToken);
        }));

    [Description("Create a temporary access pass method for a user.")]
    [McpServerTool(Title = "Add user TAP auth method", Name = "graph_auth_admin_add_tap_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_AddTemporaryAccessPassMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Lifetime in minutes (10-43200).")]
        int lifetimeInMinutes,
        [Description("Whether the pass is usable once.")] bool isUsableOnce,
        [Description("Optional UTC start date/time.")] DateTimeOffset? startDateTime,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new AddTapInput
            {
                UserId = userId,
                LifetimeInMinutes = lifetimeInMinutes,
                IsUsableOnce = isUsableOnce,
                StartDateTime = startDateTime
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/temporaryAccessPassMethods";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, new
            {
                lifetimeInMinutes = typed.LifetimeInMinutes,
                isUsableOnce = typed.IsUsableOnce,
                startDateTime = typed.StartDateTime
            }, cancellationToken);
        }));

    [Description("Enable or disable SMS sign-in on a user's phone method.")]
    [McpServerTool(Title = "Set user phone SMS sign-in", Name = "graph_auth_admin_set_phone_sms_signin", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_SetPhoneSmsSignin(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Phone method id.")] string phoneMethodId,
        [Description("True enables SMS sign-in; false disables it.")] bool enableSmsSignIn,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new SetPhoneSmsInput
            {
                UserId = userId,
                PhoneMethodId = phoneMethodId,
                EnableSmsSignIn = enableSmsSignIn
            }, cancellationToken);

            var action = typed.EnableSmsSignIn ? "enableSmsSignIn" : "disableSmsSignIn";
            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/phoneMethods/{Uri.EscapeDataString(typed.PhoneMethodId)}/{action}";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, null, cancellationToken);
        }));

    [Description("Reset a user's password method by setting a new password.")]
    [McpServerTool(Title = "Reset user password auth method", Name = "graph_auth_admin_reset_password_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_ResetPasswordMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Password method id.")] string passwordMethodId,
        [Description("New password value.")] string newPassword,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new ResetPasswordInput
            {
                UserId = userId,
                PasswordMethodId = passwordMethodId,
                NewPassword = newPassword
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/passwordMethods/{Uri.EscapeDataString(typed.PasswordMethodId)}/resetPassword";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, new
            {
                newPassword = typed.NewPassword
            }, cancellationToken);
        }));

    [Description("Delete a user's authentication method by method type segment and method id.")]
    [McpServerTool(Title = "Delete user auth method", Name = "graph_auth_admin_delete_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthAdmin_DeleteMethod(
        [Description("Microsoft Entra user id or UPN.")] string userId,
        [Description("Method segment: emailMethods, phoneMethods, fido2Methods, softwareOathMethods, temporaryAccessPassMethods, windowsHelloForBusinessMethods, platformCredentialMethods.")] string methodType,
        [Description("Authentication method id.")] string methodId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new DeleteMethodInput
            {
                UserId = userId,
                MethodType = methodType,
                MethodId = methodId
            }, cancellationToken);

            var path = $"users/{Uri.EscapeDataString(typed.UserId)}/authentication/{typed.MethodType.Trim('/')}/{Uri.EscapeDataString(typed.MethodId)}";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Delete, path, null, cancellationToken);
        }));

    private static async Task<JsonNode?> SendGraphRequestAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        using var request = new HttpRequestMessage(method, relativePath);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{(int)response.StatusCode} {response.StatusCode}: {text}");

        var graphUrl = $"https://graph.microsoft.com/beta/{relativePath.TrimStart('/')}";
        if (!string.IsNullOrWhiteSpace(text))
        {
            return JsonNode.Parse(text);
        }

        return JsonSerializer.SerializeToNode(new
        {
            method.Method,
            Path = relativePath,
            Status = (int)response.StatusCode,
            Message = "Operation completed successfully."
        });
    }

    [Description("Input for adding an email method.")]
    private sealed class AddEmailInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string EmailAddress { get; set; } = default!;
    }

    [Description("Input for adding a phone method.")]
    private sealed class AddPhoneInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string PhoneNumber { get; set; } = default!;
        [Required] public string PhoneType { get; set; } = default!;
    }

    [Description("Input for updating a phone method.")]
    private sealed class UpdatePhoneInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string PhoneMethodId { get; set; } = default!;
        [Required] public string PhoneNumber { get; set; } = default!;
        [Required] public string PhoneType { get; set; } = default!;
    }

    [Description("Input for adding a TAP method.")]
    private sealed class AddTapInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Range(10, 43200)] public int LifetimeInMinutes { get; set; }
        public bool IsUsableOnce { get; set; }
        public DateTimeOffset? StartDateTime { get; set; }
    }

    [Description("Input for phone SMS sign-in state.")]
    private sealed class SetPhoneSmsInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string PhoneMethodId { get; set; } = default!;
        public bool EnableSmsSignIn { get; set; }
    }

    [Description("Input for password reset.")]
    private sealed class ResetPasswordInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string PasswordMethodId { get; set; } = default!;
        [Required] public string NewPassword { get; set; } = default!;
    }

    [Description("Input for deleting an auth method.")]
    private sealed class DeleteMethodInput
    {
        [Required] public string UserId { get; set; } = default!;
        [Required] public string MethodType { get; set; } = default!;
        [Required] public string MethodId { get; set; } = default!;
    }
}

