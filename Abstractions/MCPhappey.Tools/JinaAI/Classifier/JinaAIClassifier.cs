using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.JinaAI.Classifier;

public static class JinaAIClassifier
{
    [Description("Classify documents in a SharePoint or OneDrive folder using a Jina AI classifier model.")]
    [McpServerTool(
        Title = "Classify SharePoint folder",
        Name = "jina_classifier_sharepoint_folder",
        ReadOnly = true)]
    public static async Task<CallToolResult?> JinaAIClassifier_SharePointFolder(
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Classifier model, jina-embeddings-v3 (text only) or jina-clip-v2 (text and images).")] string classifyModel,
         [Description("List of labels for classification.")] List<string> labels,
         [Description("SharePoint or OneDrive folder with files to classify.")] string sharepointFolderUrl,
         CancellationToken cancellationToken = default)
         => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
            await requestContext.WithStructuredContent(async () =>
            {
                var result = await graphClient.GetDriveItem(sharepointFolderUrl);
                var fileUrls = new List<string>();

                if (result?.Folder != null)
                {
                    var items = await graphClient.Drives[result.ParentReference?.DriveId!]
                        .Items[result.Id!].Children.GetAsync();

                    foreach (var item in items?.Value?.Where(a => !string.IsNullOrEmpty(a.WebUrl)) ?? [])
                        fileUrls.Add(item.WebUrl!);
                }

                return await ClassifyDocumentsAsync(
                    serviceProvider,
                    requestContext,
                    classifyModel,
                    labels,
                    fileUrls,
                    cancellationToken);
            })));


    [Description("Classify arbitrary text-based documents from a list of URLs using a Jina AI classifier model.")]
    [McpServerTool(
        Title = "Classify files",
        Name = "jina_classifier_files",
        ReadOnly = true)]
    public static async Task<CallToolResult?> JinaAIClassifier_Files(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Classifier model, jina-embeddings-v3 (text only) or jina-clip-v2 (text and images).")] string classifyModel,
        [Description("List of labels for classification.")] List<string> labels,
        [Description("List of file URLs to classify.")] List<string> fileUrls,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
               await ClassifyDocumentsAsync(
                   serviceProvider,
                   requestContext,
                   classifyModel,
                   labels,
                   fileUrls,
                   cancellationToken)));


    // ---------- Shared core logic ----------
    private static async Task<JsonNode?> ClassifyDocumentsAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string classifyModel,
        List<string> labels,
        List<string> fileUrls,
        CancellationToken cancellationToken)
    {
        var jina = serviceProvider.GetRequiredService<JinaAIClient>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var inputs = new List<string>();
        var semaphore = new SemaphoreSlim(3);

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
                        cancellationToken);

                    var textFiles = fileContents.GetTextFiles();
                    // 📝 Add text and JSON documents
                    foreach (var z in textFiles)
                    {
                        inputs.Add(z.Contents.ToString() ?? string.Empty);
                    }

                    // 🖼️ Add base64 images only for jina-clip-v2
                    if (classifyModel == "jina-clip-v2")
                    {
                        foreach (var z in fileContents
                            .Where(a => a.MimeType.StartsWith("image/")))
                        {
                            inputs.Add(Convert.ToBase64String(z.Contents.ToArray()));
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

        if (inputs.Count == 0)
            throw new Exception("No readable text found in provided files.");

        return await jina.ClassifyAsync(classifyModel, inputs, labels, cancellationToken);
    }
}
