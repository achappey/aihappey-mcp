using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.Vaults;

public static partial class AnthropicVaults
{
    [Description("Create an Anthropic vault and add the current user as an owner in metadata.")]
    [McpServerTool(Title = "Create Anthropic Vault", Name = "anthropic_vaults_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicVaults_Create(
        [Description("Human-readable vault display name.")] string displayName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional metadata JSON object. The Owners entry is controlled by this MCP server.")] string? metadataJson = null,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var userId = serviceProvider.GetUserId()
                    ?? throw new UnauthorizedAccessException("Current user id is required to create a vault.");

                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateVaultRequest
                {
                    DisplayName = displayName,
                    MetadataJson = metadataJson,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.DisplayName))
                    throw new ValidationException("displayName is required.");

                var metadata = ParseMetadataJsonOrEmpty(typed.MetadataJson, nameof(typed.MetadataJson));
                SetOwners(metadata, [userId]);

                var body = new JsonObject
                {
                    ["display_name"] = typed.DisplayName,
                    ["metadata"] = metadata
                };

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BaseUrl,
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Update an Anthropic vault. Only owners can update; the Owners metadata entry is preserved.")]
    [McpServerTool(Title = "Update Anthropic Vault", Name = "anthropic_vaults_update", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicVaults_Update(
        [Description("Vault ID to update.")] string vaultId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated display name. Omit to preserve.")] string? displayName = null,
        [Description("Optional metadata patch JSON object. Set keys to strings to upsert, or null to delete. The Owners entry is preserved.")] string? metadataPatchJson = null,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateVaultRequest
                {
                    VaultId = vaultId,
                    DisplayName = displayName,
                    MetadataPatchJson = metadataPatchJson,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedVaultId = NormalizeVaultId(typed.VaultId);
                var current = await GetOwnerVaultAsync(serviceProvider, normalizedVaultId, typed.AnthropicBetaCsv, cancellationToken);
                var owners = GetOwners(current);
                var metadataPatch = ParseMetadataPatchJsonOrNull(typed.MetadataPatchJson, nameof(typed.MetadataPatchJson));

                var body = new JsonObject();
                if (typed.DisplayName is not null)
                {
                    if (string.IsNullOrWhiteSpace(typed.DisplayName))
                        throw new ValidationException("displayName cannot be empty. Omit it to preserve the current display name.");

                    body["display_name"] = typed.DisplayName;
                }

                if (metadataPatch is not null)
                {
                    metadataPatch.Remove(OwnersKey);
                    SetOwners(metadataPatch, owners);
                    body["metadata"] = metadataPatch;
                }

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedVaultId)}",
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Add an owner to an Anthropic vault. Only current owners can add owners.")]
    [McpServerTool(Title = "Add Anthropic Vault Owner", Name = "anthropic_vaults_add_owner", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicVaults_AddOwner(
        [Description("Vault ID.")] string vaultId,
        [Description("User ID of the owner to add.")] string ownerId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicVaultOwnerRequest
                {
                    VaultId = vaultId,
                    OwnerId = ownerId,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedVaultId = NormalizeVaultId(typed.VaultId);
                var normalizedOwnerId = NormalizeId(typed.OwnerId, "ownerId");
                var current = await GetOwnerVaultAsync(serviceProvider, normalizedVaultId, typed.AnthropicBetaCsv, cancellationToken);
                var owners = GetOwners(current);

                if (!owners.Contains(normalizedOwnerId, StringComparer.OrdinalIgnoreCase))
                    owners.Add(normalizedOwnerId);

                var metadata = CloneMetadata(current);
                SetOwners(metadata, owners);

                var body = new JsonObject
                {
                    ["metadata"] = metadata
                };

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedVaultId)}",
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Archive an Anthropic vault. Only owners can archive.")]
    [McpServerTool(Title = "Archive Anthropic Vault", Name = "anthropic_vaults_archive", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicVaults_Archive(
        [Description("Vault ID to archive.")] string vaultId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveVaultRequest
                {
                    VaultId = vaultId,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedVaultId = NormalizeVaultId(typed.VaultId);
                await GetOwnerVaultAsync(serviceProvider, normalizedVaultId, typed.AnthropicBetaCsv, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedVaultId)}/archive",
                    null,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Delete an Anthropic vault after explicit typed confirmation. Only owners can delete.")]
    [McpServerTool(Title = "Delete Anthropic Vault", Name = "anthropic_vaults_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicVaults_Delete(
        [Description("Vault ID to delete.")] string vaultId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedVaultId = NormalizeVaultId(vaultId);
                await GetOwnerVaultAsync(serviceProvider, normalizedVaultId, anthropicBetaCsv, cancellationToken);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteVaultItem>(requestContext.Server, normalizedVaultId, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedVaultId)}",
                    null,
                    anthropicBetaCsv,
                    cancellationToken);
            }));
}

