using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Infomaniak;

public static class InfomaniakReranker
{
    private const string ApiBaseUrl = "https://api.infomaniak.com";

    [Description("Rerank documents in a SharePoint or OneDrive folder using an Infomaniak rerank model.")]
    [McpServerTool(Title = "Rerank SharePoint folder", Name = "infomaniak_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> Infomaniak_Rerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: BAAI/bge-reranker-v2-m3)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("SharePoint or OneDrive folder with files that should be ranked")] string sharepointFolderUrl,
        [Description("The number of top results to return.")] int topN,
        [Description("Infomaniak AI product id. If omitted, tries x-infomaniak-product-id from headers.")] int? productId = null,
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
                    productId,
                    cancellationToken);
            })));

    [Description("Rerank arbitrary text-based documents from a list of URLs using an Infomaniak rerank model.")]
    [McpServerTool(Title = "Rerank files", Name = "infomaniak_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> Infomaniak_Rerank_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model identifier (for example: BAAI/bge-reranker-v2-m3)")] string model,
        [Description("Input query to rank against")] string query,
        [Description("List of file URLs to rerank")] List<string> fileUrls,
        [Description("The number of top results to return.")] int topN,
        [Description("Infomaniak AI product id. If omitted, tries x-infomaniak-product-id from headers.")] int? productId = null,
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
                    productId,
                    cancellationToken)));

    private static async Task<JsonNode?> RerankDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string model,
        string query,
        List<string> fileUrls,
        int topN,
        int? productId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (topN < 1)
            throw new ValidationException("topN must be at least 1.");

        var settings = serviceProvider.GetRequiredService<InfomaniakSettings>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var resolvedProductId = productId ?? settings.DefaultProductId
            ?? throw new ValidationException("Missing productId. Provide it explicitly or configure x-infomaniak-product-id header.");

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

        using var client = clientFactory.CreateClient();
        client.BaseAddress = new Uri(ApiBaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        var body = new JsonObject
        {
            ["model"] = model,
            ["query"] = query,
            ["documents"] = JsonSerializer.SerializeToNode(documents),
            ["top_n"] = topN
        };

        var path = $"/2/ai/{resolvedProductId}/cohere/v2/rerank";
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(req, cancellationToken);
        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {raw}");

        return JsonNode.Parse(raw);
    }
}

