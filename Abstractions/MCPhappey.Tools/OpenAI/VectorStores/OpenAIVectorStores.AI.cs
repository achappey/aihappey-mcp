using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.VectorStores;

public static partial class OpenAIVectorStores
{
    [Description("Search a vector store for relevant chunks based on a query.")]
    [McpServerTool(Title = "Search a vector store at OpenAI", ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Search(
          [Description("The vector store id.")] string vectorStoreId,
          [Description("The vector store prompt query.")] string query,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("If the query should be rewritten.")] bool? rewriteQuery = false,
          [Description("Maximum number of results.")] int? maxNumOfResults = 10,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
            {
                var payload = new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumOfResults ?? 10,
                    ["rewrite_query"] = rewriteQuery ?? false
                };

                var content = BinaryContent.Create(BinaryData.FromObjectAsJson(payload));
                var searchResult = await client
                    .SearchVectorStoreAsync(vectorStoreId, content);
                using var raw = searchResult.GetRawResponse();            // PipelineResponse
                string json = raw.Content.ToString();         // JSON string

                return json.ToJsonCallToolResponse($"{VectorStoreExtensions.BASE_URL}/{vectorStoreId}/search");
            }, cancellationToken));

    [Description("Ask a question against an OpenAI vector store using file_search via sampling.")]
    [McpServerTool(
           Title = "Ask OpenAI vector store",
           ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAIVectorStores_Ask(
           [Description("The OpenAI vector store id.")] string vectorStoreId,
           [Description("Your question / query.")] string query,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Optional model override (defaults to gpt-5.1).")] string? model = "gpt-5.1",
           [Description("Max number of retrieved chunks.")] int? maxNumResults = 10,
           CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await serviceProvider.WithVectorStoreOwnerClient<CallToolResult?>(vectorStoreId, async (client, current) =>
           await requestContext.WithStructuredContent(async () =>
            {
                var response = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
                {
                    Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                    {
                        {"openai", new {
                            file_search = new
                            {
                                vector_store_ids = new[] { vectorStoreId },
                                max_num_results = maxNumResults ?? 10
                            },
                            reasoning = new
                            {
                                effort = "low"
                            }
                            } },
                            }),
                    Temperature = 1,
                    MaxTokens = 8192,
                    ModelPreferences = model.ToModelPreferences(),
                    Messages = [query.ToUserSamplingMessage()]
                }, cancellationToken);

                // Return the model’s final content blocks
                return response;
            })));
}

