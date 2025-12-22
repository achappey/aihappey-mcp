using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.VectorStores;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Extensions;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tools.OpenAI.VectorStores;

public static class VectorStoreExtensions
{
    public const string OWNERS_KEY = "Owners";
    public const string BASE_URL = "https://api.openai.com/v1/vector_stores";
    public const string DESCRIPTION_KEY = "Description";

    public static async Task<T> WithVectorStoreClient<T>(
        this IServiceProvider serviceProvider, Func<VectorStoreClient, Task<T>> func)
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();

        return await func(openAiClient.GetVectorStoreClient());
    }

    public static async Task<CallToolResult?> WithVectorStoreOwnerClient<T>(
        this IServiceProvider serviceProvider, string vectorStoreId, Func<VectorStoreClient, VectorStore, Task<CallToolResult?>> func, CancellationToken cancellationToken = default)
        => await serviceProvider.WithVectorStoreClient(async (client) =>
        {
            var userId = serviceProvider.GetUserId();

            var item = client
                .GetVectorStore(vectorStoreId, cancellationToken);

            if (userId == null || !item.Value.Metadata.ContainsKey(OWNERS_KEY) || !item.Value.Metadata[OWNERS_KEY].Contains(userId))
            {
                return "Only owners can perform this action".ToErrorCallToolResponse();
            }

            return await func(client, item.Value);
        });

}
