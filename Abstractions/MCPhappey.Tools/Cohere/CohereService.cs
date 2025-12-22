using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Cohere;

public static class CohereService
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using Cohere's rerank model.")]
    [McpServerTool(Title = "Cohere Rerank SharePoint Folder", Name = "cohere_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> Cohere_RerankSharePointFolder(
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        string model,
        string query,
        string sharepointFolderUrl,
        int topN,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
            await rc.WithOboGraphClient(async graphClient =>
            await rc.WithStructuredContent(async () =>
            {
                var fileUrls = await graphClient.GetFileUrlsFromFolderAsync(sharepointFolderUrl, ct);
                return await RerankDocumentsAsync(sp, rc, model, query, fileUrls, topN, ct);
            })));


    [Description("Rerank arbitrary text-based documents using Cohere's rerank API.")]
    [McpServerTool(Title = "Cohere Rerank Files", Name = "cohere_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> Cohere_RerankFiles(
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        string model,
        string query,
        List<string> fileUrls,
        int topN,
        CancellationToken ct = default)
        => await rc.WithExceptionCheck(async () =>
            await rc.WithStructuredContent(async () =>
                await RerankDocumentsAsync(sp, rc, model, query, fileUrls, topN, ct)));


    // ---------- Internal rerank logic ----------
    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider sp,
        RequestContext<CallToolRequestParams> rc,
        string model,
        string query,
        List<string> fileUrls,
        int topN,
        CancellationToken ct)
    {
        var cohere = sp.GetRequiredService<CohereClient>();
        var downloadService = sp.GetRequiredService<DownloadService>();

        var documents = new List<string>();
        var semaphore = new SemaphoreSlim(3);

        var tasks = fileUrls
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(async url =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var files = await downloadService.ScrapeContentAsync(sp, rc.Server, url, ct);
                    var textFiles = files.GetTextFiles();
                    foreach (var f in textFiles)
                        documents.Add(f.Contents.ToString());
                }
                finally
                {
                    semaphore.Release();
                }
            })
            .ToList();

        await Task.WhenAll(tasks);

        if (documents.Count == 0)
            throw new Exception("No valid text documents found for reranking.");

        return await cohere.RerankAsync(model, query, documents, topN, ct);
    }
}
