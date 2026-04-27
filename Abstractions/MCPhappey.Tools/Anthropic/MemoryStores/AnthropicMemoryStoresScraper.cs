using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.MemoryStores;

public sealed class AnthropicMemoryStoresScraper : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url)
        => url.StartsWith(AnthropicMemoryStores.BaseUrl, StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ValidationException("Invalid Anthropic memory resource URL.");

        var pathSegments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length < 2 || pathSegments[0] != "v1" || pathSegments[1] != "memory_stores")
            return [];

        var userId = serviceProvider.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedAccessException("Current user id is required to read Anthropic memory resources.");

        var node = await AnthropicManagedAgentsHttp.SendAsync(
            serviceProvider,
            HttpMethod.Get,
            url,
            null,
            null,
            cancellationToken);

        var secured = await SecureResponseAsync(serviceProvider, node, pathSegments, userId, cancellationToken);
        return [secured.ToJsonString().ToJsonFileItem(url)];
    }

    private static async Task<JsonNode> SecureResponseAsync(
        IServiceProvider serviceProvider,
        JsonNode node,
        string[] pathSegments,
        string userId,
        CancellationToken cancellationToken)
    {
        if (pathSegments.Length == 2)
        {
            var list = node as JsonObject ?? throw new ValidationException("Expected memory store list object.");
            var data = list["data"] as JsonArray ?? [];
            var filtered = new JsonArray();

            foreach (var item in data)
            {
                if (item is JsonObject memStore && memStore.IsOwner(userId))
                    filtered.Add(memStore.DeepClone());
            }

            list["data"] = filtered;
            return list;
        }

        var memoryStoreId = pathSegments[2];
        var memoryStore = pathSegments.Length == 3
            ? node as JsonObject ?? throw new ValidationException("Expected memory store object.")
            : await AnthropicMemoryStores.GetMemoryStoreAsync(serviceProvider, memoryStoreId, null, cancellationToken);

        if (!memoryStore.IsOwner(userId))
            throw new UnauthorizedAccessException("Only owners can read this Anthropic memory resource.");

        return node;
    }
}

