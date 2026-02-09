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

namespace MCPhappey.Tools.ContextualAI.Rerank;

public static class ContextualAIRerank
{
    [Description("Rerank documents in a SharePoint or OneDrive folder using Contextual AI rerank models.")]
    [McpServerTool(Title = "Rerank SharePoint folder", 
        Name = "contextualai_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> ContextualAI_Rerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model (e.g. ctxl-rerank-v2-instruct-multilingual).")]
        string rerankModel,
        [Description("Input query to rank on")]
        string query,
        [Description("SharePoint or OneDrive folder with files that should be ranked")]
        string sharepointFolderUrl,
        [Description("The number of top results to return.")]
        int topN,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
            await requestContext.WithStructuredContent(async () =>
            {
                var fileUrls = await graphClient.GetFileUrlsFromFolderAsync(sharepointFolderUrl, cancellationToken);
                return await RerankDocumentsAsync(serviceProvider, requestContext, rerankModel, query, fileUrls, topN, cancellationToken);
            })));

    [Description("Rerank arbitrary text-based documents from a list of URLs using Contextual AI rerank models.")]
    [McpServerTool(Title = "Rerank files", 
        Name = "contextualai_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> ContextualAI_Rerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model (e.g. ctxl-rerank-v2-instruct-multilingual).")]
        string rerankModel,
        [Description("Input query to rank on")]
        string query,
        [Description("File URLs to rerank (comma/semicolon/newline separated)")]
        string fileUrls,
        [Description("The number of top results to return.")]
        int topN,
        [Description("Optional custom instruction used after relevance.")]
        string instruction = "",
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
                await RerankDocumentsAsync(
                    serviceProvider,
                    requestContext,
                    rerankModel,
                    query,
                    ParseFileUrls(fileUrls),
                    topN,
                    cancellationToken,
                    instruction)));

    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string rerankModel,
        string query,
        List<string> fileUrls,
        int topN,
        CancellationToken cancellationToken,
        string instruction = "")
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var documents = new List<string>();
        var metadata = new List<string>();
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

                        documents.Add(content);
                        metadata.Add(file.Uri ?? string.Empty);
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

        using var client = serviceProvider.CreateContextualAIClient();

        var documentsArray = new JsonArray();
        foreach (var document in documents)
            documentsArray.Add(document);

        var metadataArray = new JsonArray();
        foreach (var meta in metadata)
            metadataArray.Add(meta);

        var payload = new JsonObject
        {
            ["query"] = query,
            ["documents"] = documentsArray,
            ["model"] = rerankModel,
            ["top_n"] = topN,
            ["metadata"] = metadataArray
        };

        if (!string.IsNullOrWhiteSpace(instruction))
            payload["instruction"] = instruction;

        using var request = new HttpRequestMessage(HttpMethod.Post, "rerank")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {json}");

        return JsonNode.Parse(json);
    }

    private static List<string> ParseFileUrls(string input)
        => (input ?? string.Empty)
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

