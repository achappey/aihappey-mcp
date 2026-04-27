using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemoryStores
{
    [Description("Create an Anthropic memory store and add the current user as an owner in metadata.")]
    [McpServerTool(Title = "Create Anthropic Memory Store", Name = "anthropic_memory_stores_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicMemoryStores_Create(
        [Description("Human-readable memory store name.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional memory store description.")] string? description = null,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var userId = serviceProvider.GetUserId()
                    ?? throw new UnauthorizedAccessException("Current user id is required to create a memory store.");

                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateMemoryStoreRequest
                {
                    Name = name,
                    Description = description,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.Name))
                    throw new ValidationException("name is required.");

                var metadata = new JsonObject();
                SetOwners(metadata, [userId]);

                var body = new JsonObject
                {
                    ["name"] = typed.Name,
                    ["metadata"] = metadata
                };

                SetStringIfProvided(body, "description", typed.Description);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    BaseUrl,
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Update an Anthropic memory store. Only owners can update; the Owners metadata entry is preserved.")]
    [McpServerTool(Title = "Update Anthropic Memory Store", Name = "anthropic_memory_stores_update", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicMemoryStores_Update(
        [Description("Memory store ID to update.")] string memoryStoreId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated name. Omit to preserve.")] string? name = null,
        [Description("Optional updated description. Provide an empty string to clear.")] string? description = null,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateMemoryStoreRequest
                {
                    MemoryStoreId = memoryStoreId,
                    Name = name,
                    Description = description,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedMemoryStoreId = NormalizeMemoryStoreId(typed.MemoryStoreId);
                var current = await GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId, typed.AnthropicBetaCsv, cancellationToken);
                var metadata = CloneMetadata(current);

                var body = new JsonObject
                {
                    ["metadata"] = metadata
                };

                if (typed.Name is not null)
                {
                    if (string.IsNullOrWhiteSpace(typed.Name))
                        throw new ValidationException("name cannot be empty. Omit it to preserve the current name.");

                    body["name"] = typed.Name;
                }

                SetStringIfProvided(body, "description", typed.Description);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}",
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Add an owner to an Anthropic memory store. Only current owners can add owners.")]
    [McpServerTool(Title = "Add Anthropic Memory Store Owner", Name = "anthropic_memory_stores_add_owner", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicMemoryStores_AddOwner(
        [Description("Memory store ID.")] string memoryStoreId,
        [Description("User ID of the owner to add.")] string ownerId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicMemoryStoreOwnerRequest
                {
                    MemoryStoreId = memoryStoreId,
                    OwnerId = ownerId,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedMemoryStoreId = NormalizeMemoryStoreId(typed.MemoryStoreId);
                var normalizedOwnerId = NormalizeId(typed.OwnerId, "ownerId");
                var current = await GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId, typed.AnthropicBetaCsv, cancellationToken);
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
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}",
                    body,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Archive an Anthropic memory store. Only owners can archive.")]
    [McpServerTool(Title = "Archive Anthropic Memory Store", Name = "anthropic_memory_stores_archive", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicMemoryStores_Archive(
        [Description("Memory store ID to archive.")] string memoryStoreId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicArchiveMemoryStoreRequest
                {
                    MemoryStoreId = memoryStoreId,
                    AnthropicBetaCsv = anthropicBetaCsv
                }, cancellationToken);

                var normalizedMemoryStoreId = NormalizeMemoryStoreId(typed.MemoryStoreId);
                await GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId, typed.AnthropicBetaCsv, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Post,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}/archive",
                    null,
                    typed.AnthropicBetaCsv,
                    cancellationToken);
            }));

    [Description("Delete an Anthropic memory store after explicit typed confirmation. Only owners can delete.")]
    [McpServerTool(Title = "Delete Anthropic Memory Store", Name = "anthropic_memory_stores_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicMemoryStores_Delete(
        [Description("Memory store ID to delete.")] string memoryStoreId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional extra anthropic-beta values as comma, semicolon, or newline separated strings.")] string? anthropicBetaCsv = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedMemoryStoreId = NormalizeMemoryStoreId(memoryStoreId);
                await GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId, anthropicBetaCsv, cancellationToken);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteMemoryStoreItem>(requestContext.Server, normalizedMemoryStoreId, cancellationToken);

                return await AnthropicManagedAgentsHttp.SendAsync(
                    serviceProvider,
                    HttpMethod.Delete,
                    $"{BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}",
                    null,
                    anthropicBetaCsv,
                    cancellationToken);
            }));
}

