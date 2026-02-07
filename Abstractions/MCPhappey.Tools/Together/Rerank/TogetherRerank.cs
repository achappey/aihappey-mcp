using System.ComponentModel;
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

namespace MCPhappey.Tools.Together.Rerank;

public static class TogetherRerank
{
    /* [Description("Rerank MCP server registry entries using a Together AI rerank model.")]
     [McpServerTool(Title = "Rerank MCP Server Registry", Name = "together_rerank_server_registry", ReadOnly = true)]
     public static async Task<CallToolResult?> TogetherRerank_ServerRegistry(
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Rerank model")] string rerankModel,
         [Description("Input query or prompt to rank the servers on")] string query,
         [Description("Public MCP registry JSON URL (must contain a 'servers' array)")] string registryUrl,
         [Description("The number of top results to return.")] int topN,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var settings = serviceProvider.GetRequiredService<TogetherSettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var downloadService = serviceProvider.GetRequiredService<DownloadService>();

                // --- Download registry ---
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, registryUrl, cancellationToken);
                var registryJson = files.FirstOrDefault()?.Contents.ToString() ?? throw new Exception("Registry missing");

                using var doc = JsonDocument.Parse(registryJson);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("servers", out var serversArray))
                    throw new Exception("Invalid registry format: expected { \"servers\": [...] }");

                var serverObjects = serversArray
                    .EnumerateArray()
                    .Select(s => s.GetProperty("server").GetRawText())
                    .Select(raw => JsonNode.Parse(raw)!)
                    .ToArray();

                if (serverObjects.Length == 0)
                    throw new Exception("No servers found in registry.");

                // --- Prepare rerank request body ---
                var rerankRequest = new
                {
                    query,
                    model = rerankModel,
                    return_documents = true,
                    documents = serverObjects,
                    top_n = topN
                };

                var jsonBody = JsonSerializer.Serialize(rerankRequest);

                using var rerankRequestMsg = new HttpRequestMessage(HttpMethod.Post, "https://api.together.xyz/v1/rerank")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, MimeTypes.Json)
                };

                rerankRequestMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                rerankRequestMsg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

                using var client = clientFactory.CreateClient();

                using var resp = await client.SendAsync(rerankRequestMsg, cancellationToken);
                var jsonResponse = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    throw new Exception($"{resp.StatusCode}: {jsonResponse}");

                return JsonNode.Parse(jsonResponse);
            }));*/

    [Description("List all Together AI rerank models.")]
    [McpServerTool(Title = "List Together AI rerank models", Name = "together_rerank_list_models", ReadOnly = true)]
    public static async Task<CallToolResult?> TogetherRerank_ListModels(
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
      {
          using var client = serviceProvider.CreateTogetherClient();

          using var resp = await client.GetAsync("https://api.together.xyz/v1/models", cancellationToken);
          var json = await resp.Content.ReadAsStringAsync(cancellationToken);

          if (!resp.IsSuccessStatusCode)
              throw new Exception($"{resp.StatusCode}: {json}");

          using var doc = JsonDocument.Parse(json);
          var root = doc.RootElement;

          // ✅ Works for both `{ "data": [...] }` and `[ {...}, {...} ]`
          var modelsArray = root.ValueKind == JsonValueKind.Array
              ? root.EnumerateArray()
              : root.GetProperty("data").EnumerateArray();

          var imageModels = modelsArray
              .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "rerank")
              .Select(e => JsonNode.Parse(e.GetRawText())!)
              .ToArray();

          // ✅ Always return an object, not a naked array
          return new JsonObject
          {
              ["models"] = new JsonArray(imageModels)
          };
      }));


    [Description("Rerank documents in a SharePoint or OneDrive folder using a Together AI rerank model.")]
    [McpServerTool(Title = "Rerank SharePoint Folder", Name = "together_rerank_sharepoint_folder", ReadOnly = true)]
    public static async Task<CallToolResult?> TogetherRerank_SharePointFolder(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Rerank model")] string rerankModel,
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


    [Description("Rerank arbitrary text-based documents from a list of URLs using a Together AI rerank model.")]
    [McpServerTool(Title = "Rerank Files", Name = "together_rerank_files", ReadOnly = true)]
    public static async Task<CallToolResult?> TogetherRerank_Files(
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

        var jsonBody = JsonSerializer.Serialize(new
        {
            query,
            model = rerankModel,
            return_documents = true,
            documents,
            top_n = topN
        });

        using var client = serviceProvider.CreateTogetherClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.together.xyz/v1/rerank")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await client.SendAsync(request, cancellationToken);
        var jsonResponse = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {jsonResponse}");

        return JsonNode.Parse(jsonResponse);
    }

}


