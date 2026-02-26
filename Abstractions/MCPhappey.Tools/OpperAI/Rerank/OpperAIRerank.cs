using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpperAI.Rerank;

public static class OpperAIRerank
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using Opper rerank models.")]
    [McpServerTool(Title = "Rerank SharePoint folder", Name = "opperai_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> OpperAI_Rerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model name")] string model,
        [Description("Input query to rank against")] string query,
        [Description("SharePoint or OneDrive folder URL with files to rerank")] string sharepointFolderUrl,
        [Description("Number of top documents to return. Use 0 to return all.")] int topK = 0,
        [Description("Whether to return document content in response")] bool returnDocuments = true,
        [Description("Maximum chunks per document. Use 0 for provider default.")] int maxChunksPerDoc = 0,
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
                    topK,
                    returnDocuments,
                    maxChunksPerDoc,
                    cancellationToken);
            })));

    [Description("Rerank text-based documents from file URLs using Opper rerank models.")]
    [McpServerTool(Title = "Rerank files", Name = "opperai_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> OpperAI_Rerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model name")] string model,
        [Description("Input query to rank against")] string query,
        [Description("File URLs to rerank (comma/semicolon/newline separated)")] string fileUrls,
        [Description("Number of top documents to return. Use 0 to return all.")] int topK = 0,
        [Description("Whether to return document content in response")] bool returnDocuments = true,
        [Description("Maximum chunks per document. Use 0 for provider default.")] int maxChunksPerDoc = 0,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
                await RerankDocumentsAsync(
                    serviceProvider,
                    requestContext,
                    model,
                    query,
                    ParseFileUrls(fileUrls),
                    topK,
                    returnDocuments,
                    maxChunksPerDoc,
                    cancellationToken)));

    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string model,
        string query,
        List<string> fileUrls,
        int topK,
        bool returnDocuments,
        int maxChunksPerDoc,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var opper = serviceProvider.GetRequiredService<OpperAIClient>();

        var documents = new JsonArray();
        var semaphore = new SemaphoreSlim(3);

        var tasks = fileUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(async url =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                    foreach (var file in files.GetTextFiles())
                    {
                        var content = file.Contents.ToString();
                        if (string.IsNullOrWhiteSpace(content))
                            continue;

                        documents.Add(new JsonObject
                        {
                            ["text"] = content,
                            ["metadata"] = new JsonObject
                            {
                                ["uri"] = file.Uri ?? string.Empty,
                                ["mimeType"] = file.MimeType,
                                ["filename"] = file.Filename
                            }
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
            ["query"] = query,
            ["documents"] = documents,
            ["model"] = model,
            ["return_documents"] = returnDocuments
        };

        if (topK > 0)
            payload["top_k"] = topK;

        if (maxChunksPerDoc > 0)
            payload["max_chunks_per_doc"] = maxChunksPerDoc;

        using var request = new HttpRequestMessage(HttpMethod.Post, "rerank")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        return await opper.SendAsync(request, cancellationToken);
    }

    private static List<string> ParseFileUrls(string input)
        => [.. (input ?? string.Empty)
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

