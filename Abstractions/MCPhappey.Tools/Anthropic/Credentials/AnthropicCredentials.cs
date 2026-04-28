using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Anthropic.Vaults;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Credentials;

public static partial class AnthropicCredentials
{
    [Description("Create a credential in an Anthropic vault. Only vault owners can create credentials.")]
    [McpServerTool(Title = "Create Anthropic Credential", Name = "anthropic_credentials_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicCredentials_Create(
        [Description("Vault ID.")] string vaultId,
        [Description("Credential auth JSON object. Use type static_bearer with token and mcp_server_url, or type mcp_oauth with access_token, mcp_server_url, optional expires_at, and optional refresh.")] string authJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional human-readable credential display name.")] string? displayName = null,
        [Description("Optional metadata JSON object. Owner security is inherited from the parent vault.")] string? metadataJson = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateCredentialRequest
                {
                    VaultId = vaultId,
                    AuthJson = authJson,
                    DisplayName = displayName,
                    MetadataJson = metadataJson,
                   
                }, cancellationToken);

                var normalizedVaultId = AnthropicVaults.NormalizeVaultId(typed.VaultId);
                await AnthropicVaults.GetOwnerVaultAsync(serviceProvider, normalizedVaultId,  cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.AuthJson))
                    throw new ValidationException("authJson is required.");

                var auth = AnthropicVaults.ParseJsonObject(typed.AuthJson, nameof(typed.AuthJson));
                if (string.IsNullOrWhiteSpace(auth["type"]?.GetValue<string>()))
                    throw new ValidationException("authJson.type is required.");

                var metadata = AnthropicVaults.ParseMetadataJsonOrEmpty(typed.MetadataJson, nameof(typed.MetadataJson));
                metadata.Remove(AnthropicVaults.OwnersKey);

                var body = new JsonObject
                {
                    ["auth"] = auth
                };

                AnthropicVaults.SetStringIfProvided(body, "display_name", typed.DisplayName);

                if (metadata.Count > 0)
                    body["metadata"] = metadata;

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BuildCredentialsUrl(normalizedVaultId),
                    body,
                    
                    cancellationToken);
            }));

    [Description("Update a credential in an Anthropic vault. Only vault owners can update credentials.")]
    [McpServerTool(Title = "Update Anthropic Credential", Name = "anthropic_credentials_update", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicCredentials_Update(
        [Description("Vault ID.")] string vaultId,
        [Description("Credential ID.")] string credentialId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional credential auth update JSON object. The mcp_server_url is immutable.")] string? authPatchJson = null,
        [Description("Optional updated credential display name. Omit to preserve.")] string? displayName = null,
        [Description("Optional metadata patch JSON object. Set keys to strings to upsert, or null to delete. Owner security is inherited from the parent vault.")] string? metadataPatchJson = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateCredentialRequest
                {
                    VaultId = vaultId,
                    CredentialId = credentialId,
                    AuthPatchJson = authPatchJson,
                    DisplayName = displayName,
                    MetadataPatchJson = metadataPatchJson,
                    
                }, cancellationToken);

                var normalizedVaultId = AnthropicVaults.NormalizeVaultId(typed.VaultId);
                var normalizedCredentialId = AnthropicVaults.NormalizeId(typed.CredentialId, "credentialId");
                await AnthropicVaults.GetOwnerVaultAsync(serviceProvider, normalizedVaultId,  cancellationToken);

                var body = new JsonObject();

                if (typed.AuthPatchJson is not null)
                {
                    var auth = AnthropicVaults.ParseJsonObject(typed.AuthPatchJson, nameof(typed.AuthPatchJson));
                    if (string.IsNullOrWhiteSpace(auth["type"]?.GetValue<string>()))
                        throw new ValidationException("authPatchJson.type is required when authPatchJson is provided.");

                    body["auth"] = auth;
                }

                if (typed.DisplayName is not null)
                {
                    if (string.IsNullOrWhiteSpace(typed.DisplayName))
                        throw new ValidationException("displayName cannot be empty. Omit it to preserve the current display name.");

                    body["display_name"] = typed.DisplayName;
                }

                var metadataPatch = AnthropicVaults.ParseMetadataPatchJsonOrNull(typed.MetadataPatchJson, nameof(typed.MetadataPatchJson));
                if (metadataPatch is not null)
                {
                    metadataPatch.Remove(AnthropicVaults.OwnersKey);
                    body["metadata"] = metadataPatch;
                }

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BuildCredentialUrl(normalizedVaultId, normalizedCredentialId),
                    body,
                    
                    cancellationToken);
            }));

    [Description("Archive a credential in an Anthropic vault. Only vault owners can archive credentials.")]
    [McpServerTool(Title = "Archive Anthropic Credential", Name = "anthropic_credentials_archive", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicCredentials_Archive(
        [Description("Vault ID.")] string vaultId,
        [Description("Credential ID.")] string credentialId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveCredentialRequest
                {
                    VaultId = vaultId,
                    CredentialId = credentialId,
                
                }, cancellationToken);

                var normalizedVaultId = AnthropicVaults.NormalizeVaultId(typed.VaultId);
                var normalizedCredentialId = AnthropicVaults.NormalizeId(typed.CredentialId, "credentialId");
                await AnthropicVaults.GetOwnerVaultAsync(serviceProvider, normalizedVaultId,  cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BuildCredentialUrl(normalizedVaultId, normalizedCredentialId)}/archive",
                    null,
                    
                    cancellationToken);
            }));

    [Description("Delete a credential from an Anthropic vault after explicit typed confirmation. Only vault owners can delete credentials.")]
    [McpServerTool(Title = "Delete Anthropic Credential", Name = "anthropic_credentials_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicCredentials_Delete(
        [Description("Vault ID.")] string vaultId,
        [Description("Credential ID.")] string credentialId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedVaultId = AnthropicVaults.NormalizeVaultId(vaultId);
                var normalizedCredentialId = AnthropicVaults.NormalizeId(credentialId, "credentialId");
                await AnthropicVaults.GetOwnerVaultAsync(serviceProvider, normalizedVaultId,  cancellationToken);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteCredentialItem>(requestContext.Server, $"{normalizedVaultId}:{normalizedCredentialId}", cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    BuildCredentialUrl(normalizedVaultId, normalizedCredentialId),
                    null,
                
                    cancellationToken);
            }));

    internal static string BuildCredentialsUrl(string vaultId)
        => $"{AnthropicVaults.BaseUrl}/{Uri.EscapeDataString(vaultId)}/credentials";

    internal static string BuildCredentialUrl(string vaultId, string credentialId)
        => $"{BuildCredentialsUrl(vaultId)}/{Uri.EscapeDataString(credentialId)}";
}

