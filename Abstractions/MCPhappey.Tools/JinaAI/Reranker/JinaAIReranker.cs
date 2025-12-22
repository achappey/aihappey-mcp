using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.JinaAI.Reranker;

public static class JinaAIReranker
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using a Jina AI rerank model.")]
    [McpServerTool(
         Title = "Rerank SharePoint folder",
         Name = "jina_rerank_sharepoint_folder",
         ReadOnly = true)]
    public static async Task<CallToolResult?> JinaAIRerank_SharePointFolder(
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Rerank model. jina-reranker-v2-base-multilingual (text only) or jina-reranker-m0 (text and images).")] string rerankModel,
         [Description("Input query to rank on")] string query,
         [Description("SharePoint or OneDrive folder with files that should be ranked")] string sharepointFolderUrl,
         [Description("The number of top results to return.")] int topN,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
            await requestContext.WithStructuredContent(async () =>
            {
                var fileUrls = await graphClient.GetFileUrlsFromFolderAsync(sharepointFolderUrl, cancellationToken);
                return await RerankDocumentsAsync(serviceProvider, requestContext, rerankModel, query, fileUrls, topN, cancellationToken);
            })));


    [Description("Rerank arbitrary text-based documents from a list of URLs using a Jina AI rerank model.")]
    [McpServerTool(
        Title = "Rerank files",
        Name = "jina_rerank_files",
        ReadOnly = true)]
    public static async Task<CallToolResult?> JinaAIRerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model. jina-reranker-v2-base-multilingual (text only) or jina-reranker-m0 (text and images).")] string rerankModel,
        [Description("Input query to rank on")] string query,
        [Description("List of file URLs to rerank")] List<string> fileUrls,
        [Description("The number of top results to return.")] int topN,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
               await RerankDocumentsAsync(serviceProvider, requestContext, rerankModel, query, fileUrls, topN, cancellationToken)));


    // ---------- Shared core logic ----------
    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string rerankModel,
        string query,
        List<string> fileUrls,
        int topN,
        CancellationToken cancellationToken)
    {
        var jina = serviceProvider.GetRequiredService<JinaAIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var documents = new List<string>();
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

                    // 📝 Add text and JSON documents
                    foreach (var z in files.GetTextFiles())
                    {
                        documents.Add(z.Contents.ToString() ?? string.Empty);
                    }

                    // 🖼️ Add base64 images only for jina-reranker-m0
                    if (rerankModel == "jina-reranker-m0")
                    {
                        foreach (var z in files.Where(a => a.MimeType.StartsWith("image/")))
                        {
                            documents.Add(Convert.ToBase64String(z.Contents.ToArray()));
                        }
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

        return await jina.RerankAsync(rerankModel, query, documents, topN, cancellationToken);
    }

}

