using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.VoyageAI.Rerank;

public static class VoyageAIReranker
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using a Voyage AI rerankers.")]
    [McpServerTool(Title = "Rerank SharePoint Folder", Name = "voyageai_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> VoyageAIRerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model. rerank-2.5 or rerank-2.5-lite")] string rerankModel,
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


    [Description("Rerank arbitrary text-based documents from a list of URLs using a Voyage AI rerankers.")]
    [McpServerTool(Title = "Rerank Files", Name = "voyageai_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> VoyageAIRerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model")] string rerankModel,
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
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var voyageAIClient = serviceProvider.GetRequiredService<VoyageAIClient>();

        var documents = new List<object>();
        var semaphore = new SemaphoreSlim(3); // limit to 3 concurrent downloads

        var tasks = fileUrls
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(async url =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var fileContents = await downloadService.ScrapeContentAsync(
                        serviceProvider,
                        requestContext.Server,
                        url,
                        cancellationToken
                    );

                    foreach (var z in fileContents.GetTextFiles())
                    {
                        documents.Add(new
                        {
                            uri = z.Uri,
                            mimeType = z.MimeType,
                            filename = z.Filename,
                            contents = z.Contents.ToString(),
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
            throw new Exception("No content found in provided files.");

        return await voyageAIClient.RerankAsync(rerankModel, query,
            [.. documents.Select(a => JsonSerializer.Serialize(a))], topN, true, cancellationToken);
    }

}


