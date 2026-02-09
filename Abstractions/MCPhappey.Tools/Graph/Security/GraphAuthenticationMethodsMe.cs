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

public static class GraphAuthenticationMethodsMe
{
    [Description("Add an email authentication method for the current user.")]
    [McpServerTool(Title = "Add my email auth method", Name = "graph_auth_me_add_email_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_AddEmailMethod(
        [Description("Email address to register.")] string emailAddress,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(new AddEmailInput
            {
                EmailAddress = emailAddress
            }, cancellationToken);

            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, "me/authentication/emailMethods", new
            {
                emailAddress = typed.EmailAddress
            }, cancellationToken);
        }));

    [Description("Add a phone authentication method for the current user.")]
    [McpServerTool(Title = "Add my phone auth method", Name = "graph_auth_me_add_phone_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_AddPhoneMethod(
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
                PhoneNumber = phoneNumber,
                PhoneType = phoneType
            }, cancellationToken);

            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, "me/authentication/phoneMethods", new
            {
                phoneNumber = typed.PhoneNumber,
                phoneType = typed.PhoneType
            }, cancellationToken);
        }));

    [Description("Update a phone authentication method for the current user.")]
    [McpServerTool(Title = "Update my phone auth method", Name = "graph_auth_me_update_phone_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_UpdatePhoneMethod(
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
                PhoneMethodId = phoneMethodId,
                PhoneNumber = phoneNumber,
                PhoneType = phoneType
            }, cancellationToken);

            var path = $"me/authentication/phoneMethods/{Uri.EscapeDataString(typed.PhoneMethodId)}";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Patch, path, new
            {
                phoneNumber = typed.PhoneNumber,
                phoneType = typed.PhoneType
            }, cancellationToken);
        }));

    [Description("Create a temporary access pass method for the current user.")]
    [McpServerTool(Title = "Add my TAP auth method", Name = "graph_auth_me_add_tap_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_AddTemporaryAccessPassMethod(
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
                LifetimeInMinutes = lifetimeInMinutes,
                IsUsableOnce = isUsableOnce,
                StartDateTime = startDateTime
            }, cancellationToken);

            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, "me/authentication/temporaryAccessPassMethods", new
            {
                lifetimeInMinutes = typed.LifetimeInMinutes,
                isUsableOnce = typed.IsUsableOnce,
                startDateTime = typed.StartDateTime
            }, cancellationToken);
        }));

    [Description("Enable or disable SMS sign-in on a current-user phone method.")]
    [McpServerTool(Title = "Set my phone SMS sign-in", Name = "graph_auth_me_set_phone_sms_signin", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_SetPhoneSmsSignin(
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
                PhoneMethodId = phoneMethodId,
                EnableSmsSignIn = enableSmsSignIn
            }, cancellationToken);

            var action = typed.EnableSmsSignIn ? "enableSmsSignIn" : "disableSmsSignIn";
            var path = $"me/authentication/phoneMethods/{Uri.EscapeDataString(typed.PhoneMethodId)}/{action}";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, null, cancellationToken);
        }));

    [Description("Reset the current user's password method by setting a new password.")]
    [McpServerTool(Title = "Reset my password auth method", Name = "graph_auth_me_reset_password_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_ResetPasswordMethod(
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
                PasswordMethodId = passwordMethodId,
                NewPassword = newPassword
            }, cancellationToken);

            var path = $"me/authentication/passwordMethods/{Uri.EscapeDataString(typed.PasswordMethodId)}/resetPassword";
            return await SendGraphRequestAsync(serviceProvider, requestContext, HttpMethod.Post, path, new
            {
                newPassword = typed.NewPassword
            }, cancellationToken);
        }));

    [Description("Delete one authentication method from the current user by method type segment and method id.")]
    [McpServerTool(Title = "Delete my auth method", Name = "graph_auth_me_delete_method", OpenWorld = false, ReadOnly = false, Destructive = true)]
    public static async Task<CallToolResult?> GraphAuthMe_DeleteMethod(
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
                MethodType = methodType,
                MethodId = methodId
            }, cancellationToken);

            var path = $"me/authentication/{typed.MethodType.Trim('/')}/{Uri.EscapeDataString(typed.MethodId)}";
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

    [Description("Input for adding an email authentication method for the current user.")]
    private sealed class AddEmailInput
    {
        [Required]
        [Description("Email address to register as an authentication method.")]
        public string EmailAddress { get; set; } = default!;
    }

    [Description("Input for adding a phone authentication method for the current user.")]
    private sealed class AddPhoneInput
    {
        [Required]
        [Description("Phone number in E.164 format, for example +31612345678.")]
        public string PhoneNumber { get; set; } = default!;

        [Required]
        [Description("Type of phone number. Allowed values: mobile, alternateMobile, office.")]
        public string PhoneType { get; set; } = default!;
    }

    [Description("Input for updating an existing phone authentication method.")]
    private sealed class UpdatePhoneInput
    {
        [Required]
        [Description("Identifier of the existing phone authentication method.")]
        public string PhoneMethodId { get; set; } = default!;

        [Required]
        [Description("New phone number in E.164 format, for example +31612345678.")]
        public string PhoneNumber { get; set; } = default!;

        [Required]
        [Description("Type of phone number. Allowed values: mobile, alternateMobile, office.")]
        public string PhoneType { get; set; } = default!;
    }

    [Description("Input for creating a temporary access pass authentication method.")]
    private sealed class AddTapInput
    {
        [Range(10, 43200)]
        [Description("Lifetime of the temporary access pass in minutes. Allowed range: 10 to 43200 minutes.")]
        public int LifetimeInMinutes { get; set; }

        [Description("Indicates whether the temporary access pass can be used only once.")]
        public bool IsUsableOnce { get; set; }

        [Description("Optional UTC start date and time when the temporary access pass becomes valid.")]
        public DateTimeOffset? StartDateTime { get; set; }
    }

    [Description("Input for enabling or disabling SMS sign-in on a phone authentication method.")]
    private sealed class SetPhoneSmsInput
    {
        [Required]
        [Description("Identifier of the phone authentication method.")]
        public string PhoneMethodId { get; set; } = default!;

        [Description("True enables SMS sign-in; false disables SMS sign-in.")]
        public bool EnableSmsSignIn { get; set; }
    }

    [Description("Input for resetting the password authentication method.")]
    private sealed class ResetPasswordInput
    {
        [Required]
        [Description("Identifier of the password authentication method.")]
        public string PasswordMethodId { get; set; } = default!;

        [Required]
        [Description("New password value to set for the current user.")]
        public string NewPassword { get; set; } = default!;
    }

    [Description("Input for deleting an authentication method from the current user.")]
    private sealed class DeleteMethodInput
    {
        [Required]
        [Description("Authentication method segment. Examples: emailMethods, phoneMethods, fido2Methods, softwareOathMethods, temporaryAccessPassMethods, windowsHelloForBusinessMethods, platformCredentialMethods.")]
        public string MethodType { get; set; } = default!;

        [Required]
        [Description("Identifier of the authentication method to delete.")]
        public string MethodId { get; set; } = default!;
    }

}

