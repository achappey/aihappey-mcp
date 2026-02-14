using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Scaleway.Rerank;

public static class ScalewayRerank
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using a Scaleway rerank model.")]
    [McpServerTool(Title = "Rerank SharePoint folder", Name = "scaleway_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> Scaleway_Rerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: qwen3-embedding-8b)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("SharePoint or OneDrive folder with files that should be ranked")] string sharepointFolderUrl,
        [Description("The number of top results to return.")] int topN,
        [Description("Optional Scaleway project id. If omitted, your default project is used.")] string? projectId = null,
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
                    projectId,
                    cancellationToken);
            })));

    [Description("Rerank arbitrary text-based documents from a list of URLs using a Scaleway rerank model.")]
    [McpServerTool(Title = "Rerank files", Name = "scaleway_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> Scaleway_Rerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: qwen3-embedding-8b)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("List of file URLs to rerank")] List<string> fileUrls,
        [Description("The number of top results to return.")] int topN,
        [Description("Optional Scaleway project id. If omitted, your default project is used.")] string? projectId = null,
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
                    projectId,
                    cancellationToken)));

    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string model,
        string query,
        List<string> fileUrls,
        int topN,
        string? projectId,
        CancellationToken cancellationToken)
    {
        var scaleway = serviceProvider.GetRequiredService<ScalewayClient>();
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

                    foreach (var file in files.GetTextFiles())
                    {
                        var text = file.Contents.ToString();
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        documents.Add(text);
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

        var body = new
        {
            model,
            query,
            documents,
            top_n = topN
        };

        return await scaleway.PostRerankAsync(projectId, body, cancellationToken);
    }
}

