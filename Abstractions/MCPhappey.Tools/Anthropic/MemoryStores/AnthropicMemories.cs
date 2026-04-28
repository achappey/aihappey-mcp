using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public static partial class AnthropicMemories
{
    [Description("Create a memory in an Anthropic memory store. Only memory store owners can create memories.")]
    [McpServerTool(Title = "Create Anthropic Memory", Name = "anthropic_memories_create", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicMemories_Create(
        [Description("Memory store ID.")] string memoryStoreId,
        [Description("Memory path.")] string path,
        [Description("Memory content.")] string content,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional response view. Allowed values: basic or full.")] string? view = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicCreateMemoryRequest
                {
                    MemoryStoreId = memoryStoreId,
                    Path = path,
                    Content = content,
                    View = view,
                   
                }, cancellationToken);

                var normalizedMemoryStoreId = AnthropicMemoryStores.NormalizeMemoryStoreId(typed.MemoryStoreId);
                await AnthropicMemoryStores.GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId,  cancellationToken);

                if (string.IsNullOrWhiteSpace(typed.Path))
                    throw new ValidationException("path is required.");

                if (string.IsNullOrWhiteSpace(typed.Content))
                    throw new ValidationException("content is required.");

                var body = new JsonObject
                {
                    ["path"] = typed.Path,
                    ["content"] = typed.Content
                };

                var url = BuildMemoriesUrl(normalizedMemoryStoreId, typed.View);
                return await AnthropicManagedAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, url, body,  cancellationToken);
            }));

    [Description("Update a memory in an Anthropic memory store. Only memory store owners can update memories.")]
    [McpServerTool(Title = "Update Anthropic Memory", Name = "anthropic_memories_update", ReadOnly = false, OpenWorld = false, Destructive = false)]
    public static async Task<CallToolResult?> AnthropicMemories_Update(
        [Description("Memory store ID.")] string memoryStoreId,
        [Description("Memory ID.")] string memoryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional updated memory path. Omit to preserve.")] string? path = null,
        [Description("Optional updated memory content. Omit to preserve.")] string? content = null,
        [Description("Optional content SHA256 precondition.")] string? expectedContentSha256 = null,
        [Description("Optional response view. Allowed values: basic or full.")] string? view = null,
        
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(new AnthropicUpdateMemoryRequest
                {
                    MemoryStoreId = memoryStoreId,
                    MemoryId = memoryId,
                    Path = path,
                    Content = content,
                    ExpectedContentSha256 = expectedContentSha256,
                    View = view,
                   
                }, cancellationToken);

                var normalizedMemoryStoreId = AnthropicMemoryStores.NormalizeMemoryStoreId(typed.MemoryStoreId);
                var normalizedMemoryId = AnthropicMemoryStores.NormalizeId(typed.MemoryId, "memoryId");
                await AnthropicMemoryStores.GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId,  cancellationToken);

                var body = new JsonObject();
                AnthropicMemoryStores.SetStringIfProvided(body, "path", typed.Path);
                AnthropicMemoryStores.SetStringIfProvided(body, "content", typed.Content);

                if (!string.IsNullOrWhiteSpace(typed.ExpectedContentSha256))
                {
                    body["precondition"] = new JsonObject
                    {
                        ["type"] = "content_sha256",
                        ["content_sha256"] = typed.ExpectedContentSha256
                    };
                }

                var url = BuildMemoryUrl(normalizedMemoryStoreId, normalizedMemoryId, typed.View);
                return await AnthropicManagedAgentsHttp.SendAsync(serviceProvider, HttpMethod.Post, url, body,  cancellationToken);
            }));

    [Description("Delete a memory from an Anthropic memory store after explicit typed confirmation. Only memory store owners can delete memories.")]
    [McpServerTool(Title = "Delete Anthropic Memory", Name = "anthropic_memories_delete", ReadOnly = false, OpenWorld = false, Destructive = true)]
    public static async Task<CallToolResult?> AnthropicMemories_Delete(
        [Description("Memory store ID.")] string memoryStoreId,
        [Description("Memory ID.")] string memoryId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional expected content SHA256.")] string? expectedContentSha256 = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var normalizedMemoryStoreId = AnthropicMemoryStores.NormalizeMemoryStoreId(memoryStoreId);
                var normalizedMemoryId = AnthropicMemoryStores.NormalizeId(memoryId, "memoryId");
                await AnthropicMemoryStores.GetOwnerMemoryStoreAsync(serviceProvider, normalizedMemoryStoreId,  cancellationToken);
                await AnthropicManagedAgentsHttp.ConfirmDeleteAsync<AnthropicDeleteMemoryItem>(requestContext.Server, $"{normalizedMemoryStoreId}:{normalizedMemoryId}", cancellationToken);

                var url = $"{AnthropicMemoryStores.BaseUrl}/{Uri.EscapeDataString(normalizedMemoryStoreId)}/memories/{Uri.EscapeDataString(normalizedMemoryId)}";
                if (!string.IsNullOrWhiteSpace(expectedContentSha256))
                    url = QueryHelpers.AddQueryString(url, "expected_content_sha256", expectedContentSha256);

                return await AnthropicManagedAgentsHttp.SendAsync(serviceProvider, HttpMethod.Delete, url, null,  cancellationToken);
            }));

    internal static string BuildMemoriesUrl(string memoryStoreId, string? view)
    {
        var url = $"{AnthropicMemoryStores.BaseUrl}/{Uri.EscapeDataString(memoryStoreId)}/memories";
        return AddView(url, view);
    }

    internal static string BuildMemoryUrl(string memoryStoreId, string memoryId, string? view)
    {
        var url = $"{AnthropicMemoryStores.BaseUrl}/{Uri.EscapeDataString(memoryStoreId)}/memories/{Uri.EscapeDataString(memoryId)}";
        return AddView(url, view);
    }

    internal static string AddView(string url, string? view)
    {
        if (string.IsNullOrWhiteSpace(view))
            return url;

        if (!string.Equals(view, "basic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(view, "full", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("view must be 'basic' or 'full'.");
        }

        return QueryHelpers.AddQueryString(url, "view", view.Trim().ToLowerInvariant());
    }
}

