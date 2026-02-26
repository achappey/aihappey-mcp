using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Pinecone.Rerank;

public static class PineconeRerank
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using a Pinecone rerank model.")]
    [McpServerTool(Title = "Rerank SharePoint folder", Name = "pinecone_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> Pinecone_Rerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: bge-reranker-v2-m3)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("SharePoint or OneDrive folder with files that should be ranked")] string sharepointFolderUrl,
        [Description("The number of top results to return.")] int topN,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graphClient =>
            await requestContext.WithStructuredContent(async () =>
            {
                var fileUrls = await graphClient.GetFileUrlsFromFolderAsync(sharepointFolderUrl, cancellationToken);

                return await RerankDocumentsAsync(
                    serviceProvider,
                    requestContext,
                    model,
                    query,
                    fileUrls,
                    topN,
                    cancellationToken);
            })));

    [Description("Rerank arbitrary text-based documents from a list of URLs using a Pinecone rerank model.")]
    [McpServerTool(Title = "Rerank files", Name = "pinecone_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> Pinecone_Rerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: bge-reranker-v2-m3)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("List of file URLs to rerank")] List<string> fileUrls,
        [Description("The number of top results to return.")] int topN,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
                await RerankDocumentsAsync(
                    serviceProvider,
                    requestContext,
                    model,
                    query,
                    fileUrls,
                    topN,
                    cancellationToken)));

    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string model,
        string query,
        List<string> fileUrls,
        int topN,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (topN < 1)
            throw new ValidationException("topN must be at least 1.");

        var client = serviceProvider.GetRequiredService<PineconeClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var documents = new List<JsonObject>();
        var semaphore = new SemaphoreSlim(3);

        var tasks = fileUrls
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(async url =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var files = await downloadService.ScrapeContentAsync(
                        serviceProvider,
                        requestContext.Server,
                        url,
                        cancellationToken);

                    foreach (var file in files.GetTextFiles())
                    {
                        var text = file.Contents.ToString();
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        documents.Add(new JsonObject
                        {
                            ["id"] = file.Uri ?? file.Filename ?? Guid.NewGuid().ToString("N"),
                            ["text"] = text
                        });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToList();

        await Task.WhenAll(tasks);

        if (documents.Count == 0)
            throw new Exception("No readable content found in provided files.");

        var payload = new JsonObject
        {
            ["model"] = model,
            ["query"] = query,
            ["return_documents"] = true,
            ["top_n"] = topN,
            ["documents"] = new JsonArray([.. documents])
        };

        return await client.RerankAsync(payload, cancellationToken);
    }
}
