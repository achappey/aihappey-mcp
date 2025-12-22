using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using OpenAI;

namespace MCPhappey.Tools.OpenAI.VectorStores;

public class VectorStoreScraper : IContentScraper
{

    public bool SupportsHost(ServerConfig serverConfig, string url)
        => url.StartsWith(VectorStoreExtensions.BASE_URL, StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer mcpServer,
        IServiceProvider serviceProvider, string url, CancellationToken cancellationToken = default)
    {
        
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();
        var userId = serviceProvider.GetUserId();
        var client = openAiClient
                    .GetVectorStoreClient();

        if (url.Equals(VectorStoreExtensions.BASE_URL, StringComparison.OrdinalIgnoreCase))
        {
            var item = await client.GetVectorStoresAsync(cancellationToken: cancellationToken).MaterializeToListAsync(cancellationToken: cancellationToken);

            return [item
                .Where(a => a.IsOwner(userId))
                .ToFileItem(url)];
        }
        else

        if (url.StartsWith($"{VectorStoreExtensions.BASE_URL}/vs_", StringComparison.OrdinalIgnoreCase)
            && url.EndsWith("/files", StringComparison.OrdinalIgnoreCase))
        {
            string? vectorStoreId = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var segs = uri.AbsolutePath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 4 && segs[1] == "vector_stores" && segs[^1] == "files")
                    vectorStoreId = segs[2];
            }

            var list = client.GetVectorStore(vectorStoreId, cancellationToken: cancellationToken);

            if (!list.Value.IsOwner(userId))
            {
                throw new UnauthorizedAccessException();
            }

            var refs = await client.GetVectorStoreFilesAsync(vectorStoreId, cancellationToken: cancellationToken)
                .MaterializeToListAsync(cancellationToken: cancellationToken);

            return refs.Select(a => a.ToFileItem(url));
        }

        return [];
    }

}
