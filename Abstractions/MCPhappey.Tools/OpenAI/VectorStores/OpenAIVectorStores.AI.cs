using System.ClientModel;
using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.VectorStores;

public static partial class OpenAIVectorStores
{
    [Description("Search a vector store for relevant chunks based on a query.")]
    [McpServerTool(Title = "Search a vector store at OpenAI", ReadOnly = true, OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Search(
        [Description("The vector store id.")] string vectorStoreId,
        [Description("The vector store prompt query.")] string query,
        IServiceProvider serviceProvider,
        [Description("If the query should be rewritten.")] bool? rewriteQuery = false,
        [Description("Maximum number of results.")] int? maxNumOfResults = 10,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
            {
                var payload = new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumOfResults ?? 10,
                    ["rewrite_query"] = rewriteQuery ?? false
                };

                var content = BinaryContent.Create(BinaryData.FromObjectAsJson(payload));
                var searchResult = await client.SearchVectorStoreAsync(vectorStoreId, content);
                using var raw = searchResult.GetRawResponse();

                return raw.Content.ToString().ToJsonCallToolResponse(
                    $"{VectorStoreExtensions.BASE_URL}/{vectorStoreId}/search");
            }, cancellationToken));

    [Description("Ask a question against an OpenAI vector store using file search.")]
    [McpServerTool(Title = "Ask OpenAI vector store", ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Ask(
        [Description("The OpenAI vector store id.")] string vectorStoreId,
        [Description("Your question / query.")] string query,
        IServiceProvider serviceProvider,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        [Description("Max number of retrieved chunks.")] int? maxNumResults = 10,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var responses = serviceProvider.GetRequiredService<OpenAIResponsesClient>();
                var response = await responses.CreateResponseAsync(new JsonObject
                {
                    ["model"] = OpenAIResponsesClient.ResolveModel(model),
                    ["input"] = query,
                    ["reasoning"] = new JsonObject { ["effort"] = "low" },
                    ["tools"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "file_search",
                            ["vector_store_ids"] = new JsonArray(vectorStoreId),
                            ["max_num_results"] = maxNumResults ?? 10
                        }
                    }
                }, cancellationToken);

                return (OpenAIResponsesClient.GetOutputText(response)
                    ?? throw new InvalidOperationException("The OpenAI Responses API returned no text output."))
                    .ToTextCallToolResponse();
            }, cancellationToken));
}
