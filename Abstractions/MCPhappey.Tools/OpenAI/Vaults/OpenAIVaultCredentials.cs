using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.AgentsApi;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.Vaults;

public static partial class OpenAIVaults
{
    [Description("Create a static bearer credential in an OpenAI vault. The token is write-only and never included in the result.")]
    [McpServerTool(Title = "Create OpenAI Static Bearer Credential", Name = "openai_vault_credentials_create_static_bearer", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIVaultCredentials_CreateStaticBearer(
        string vaultId, string name, string mcpServerUrl, string token,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new CreateStaticBearerRequest
            { VaultId = vaultId, Name = name, McpServerUrl = mcpServerUrl, Token = token }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                Required(input.VaultId, "vaultId"); Required(input.Name, "name"); Required(input.Token, "token");
                OpenAIAgentsHttp.ValidateHttpsUrl(input.McpServerUrl, "mcpServerUrl");
                var body = new JsonObject
                {
                    ["name"] = input.Name,
                    ["auth"] = new JsonObject { ["type"] = "static_bearer", ["token"] = input.Token, ["mcp_server_url"] = input.McpServerUrl }
                };
                return await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, CredentialsUrl(input.VaultId), body, cancellationToken);
            });
        });

    [Description("Create an MCP OAuth credential in an OpenAI vault with optional refresh configuration. Tokens and client secrets are write-only.")]
    [McpServerTool(Title = "Create OpenAI MCP OAuth Credential", Name = "openai_vault_credentials_create_oauth", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIVaultCredentials_CreateOAuth(
        string vaultId, string name, string mcpServerUrl, string accessToken,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? expiresAt = null, string? refreshToken = null, string? clientId = null, string? tokenEndpoint = null,
        string? tokenEndpointAuth = null, string? clientSecret = null, string? resource = null, string? scope = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new CreateOAuthRequest
            {
                VaultId = vaultId, Name = name, McpServerUrl = mcpServerUrl, AccessToken = accessToken, ExpiresAt = expiresAt,
                RefreshToken = refreshToken, ClientId = clientId, TokenEndpoint = tokenEndpoint, TokenEndpointAuth = tokenEndpointAuth,
                ClientSecret = clientSecret, Resource = resource, Scope = scope
            }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                Required(input.VaultId, "vaultId"); Required(input.Name, "name"); Required(input.AccessToken, "accessToken");
                OpenAIAgentsHttp.ValidateHttpsUrl(input.McpServerUrl, "mcpServerUrl"); ValidateTimestamp(input.ExpiresAt, "expiresAt");
                var auth = new JsonObject { ["type"] = "mcp_oauth", ["access_token"] = input.AccessToken, ["mcp_server_url"] = input.McpServerUrl };
                if (input.ExpiresAt is not null) auth["expires_at"] = input.ExpiresAt;
                var refreshSupplied = new[] { input.RefreshToken, input.ClientId, input.TokenEndpoint, input.TokenEndpointAuth, input.ClientSecret, input.Resource, input.Scope }.Any(x => x is not null);
                if (refreshSupplied) auth["refresh"] = BuildRefreshCreate(input);
                return await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, CredentialsUrl(input.VaultId),
                    new JsonObject { ["name"] = input.Name, ["auth"] = auth }, cancellationToken);
            });
        });

    private static JsonObject BuildRefreshCreate(CreateOAuthRequest input)
    {
        Required(input.RefreshToken, "refreshToken"); Required(input.ClientId, "clientId"); Required(input.TokenEndpoint, "tokenEndpoint");
        OpenAIAgentsHttp.ValidateHttpsUrl(input.TokenEndpoint!, "tokenEndpoint"); ValidateTokenAuth(input.TokenEndpointAuth);
        var authType = input.TokenEndpointAuth ?? "none";
        if (authType != "none") Required(input.ClientSecret, "clientSecret");
        if (authType == "none" && input.ClientSecret is not null) throw new ValidationException("clientSecret is invalid for tokenEndpointAuth none.");
        var endpointAuth = new JsonObject { ["type"] = authType };
        if (input.ClientSecret is not null) endpointAuth["client_secret"] = input.ClientSecret;
        var refresh = new JsonObject
        {
            ["refresh_token"] = input.RefreshToken, ["client_id"] = input.ClientId,
            ["token_endpoint"] = input.TokenEndpoint, ["token_endpoint_auth"] = endpointAuth
        };
        if (input.Resource is not null) refresh["resource"] = input.Resource;
        if (input.Scope is not null) refresh["scope"] = input.Scope;
        return refresh;
    }

    [Description("Rotate a static bearer vault credential. The replacement token is write-only.")]
    [McpServerTool(Title = "Rotate OpenAI Static Bearer Credential", Name = "openai_vault_credentials_rotate_static_bearer", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIVaultCredentials_RotateStaticBearer(
        string vaultId, string credentialId, string token,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new RotateStaticBearerRequest
            { VaultId = vaultId, CredentialId = credentialId, Token = token }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                Required(input.VaultId, "vaultId"); Required(input.CredentialId, "credentialId"); Required(input.Token, "token");
                return await Rotate(serviceProvider, input.VaultId, input.CredentialId,
                    new JsonObject { ["auth"] = new JsonObject { ["type"] = "static_bearer", ["token"] = input.Token } }, cancellationToken);
            });
        });

    [Description("Rotate OAuth credential access/refresh secrets and mutable expiry, scope, or client-secret values. Omitted secrets are preserved.")]
    [McpServerTool(Title = "Rotate OpenAI MCP OAuth Credential", Name = "openai_vault_credentials_rotate_oauth", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenAIVaultCredentials_RotateOAuth(
        string vaultId, string credentialId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? accessToken = null, string? expiresAt = null, bool clearExpiresAt = false,
        string? refreshToken = null, string? scope = null, bool clearScope = false,
        string? tokenEndpointAuth = null, string? clientSecret = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(new RotateOAuthRequest
            {
                VaultId = vaultId, CredentialId = credentialId, AccessToken = accessToken, ExpiresAt = expiresAt,
                ClearExpiresAt = clearExpiresAt, RefreshToken = refreshToken, Scope = scope, ClearScope = clearScope,
                TokenEndpointAuth = tokenEndpointAuth, ClientSecret = clientSecret
            }, cancellationToken);
            if (rejected is not null) return rejected;
            return await requestContext.WithStructuredContent(async () =>
            {
                Required(input.VaultId, "vaultId"); Required(input.CredentialId, "credentialId");
                if (input.ClearExpiresAt && input.ExpiresAt is not null) throw new ValidationException("expiresAt and clearExpiresAt cannot be used together.");
                if (input.ClearScope && input.Scope is not null) throw new ValidationException("scope and clearScope cannot be used together.");
                ValidateTimestamp(input.ExpiresAt, "expiresAt"); ValidateTokenAuth(input.TokenEndpointAuth);
                var auth = new JsonObject { ["type"] = "mcp_oauth" };
                if (input.AccessToken is not null) auth["access_token"] = input.AccessToken;
                if (input.ClearExpiresAt) auth["expires_at"] = null; else if (input.ExpiresAt is not null) auth["expires_at"] = input.ExpiresAt;
                var refreshRequired = input.RefreshToken is not null || input.Scope is not null || input.ClearScope || input.TokenEndpointAuth is not null || input.ClientSecret is not null;
                if (refreshRequired)
                {
                    var refresh = new JsonObject();
                    if (input.RefreshToken is not null) refresh["refresh_token"] = input.RefreshToken;
                    if (input.ClearScope) refresh["scope"] = null; else if (input.Scope is not null) refresh["scope"] = input.Scope;
                    if (input.TokenEndpointAuth is not null)
                    {
                        if (input.TokenEndpointAuth == "none") throw new ValidationException("OAuth rotation only supports client_secret_basic or client_secret_post endpoint authentication updates.");
                        var endpointAuth = new JsonObject { ["type"] = input.TokenEndpointAuth };
                        if (input.ClientSecret is not null) endpointAuth["client_secret"] = input.ClientSecret;
                        refresh["token_endpoint_auth"] = endpointAuth;
                    }
                    else if (input.ClientSecret is not null) throw new ValidationException("tokenEndpointAuth is required when clientSecret is provided.");
                    auth["refresh"] = refresh;
                }
                if (auth.Count == 1) throw new ValidationException("At least one OAuth rotation value is required.");
                return await Rotate(serviceProvider, input.VaultId, input.CredentialId, new JsonObject { ["auth"] = auth }, cancellationToken);
            });
        });

    [Description("Permanently delete a credential from an OpenAI vault after typed confirmation.")]
    [McpServerTool(Title = "Delete OpenAI Vault Credential", Name = "openai_vault_credentials_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenAIVaultCredentials_Delete(
        string vaultId, string credentialId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            Required(vaultId, "vaultId"); Required(credentialId, "credentialId");
            var confirmation = $"{vaultId}:{credentialId}";
            return await requestContext.ConfirmAndDeleteAsync<DeleteItem>(confirmation,
                async ct => await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Delete, CredentialUrl(vaultId, credentialId), null, ct),
                $"OpenAI credential '{credentialId}' deleted successfully.", cancellationToken);
        });

    private static async Task<JsonNode> Rotate(IServiceProvider serviceProvider, string vaultId, string credentialId, JsonObject body, CancellationToken cancellationToken)
        => await OpenAIAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, CredentialUrl(vaultId, credentialId), body, cancellationToken);
}
