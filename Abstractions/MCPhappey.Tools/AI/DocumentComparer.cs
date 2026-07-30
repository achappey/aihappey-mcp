using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentComparer
{
    [Description("Parallel document comparer across multiple AI models.")]
    [McpServerTool(Title = "Document comparer (multi-model)",
        Name = "document_comparer_compare",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentComparer_Compare(
       [Description("Url of the original document you would like to compare. Protected SharePoint and OneDive links are supported.")] string originalFileUrl,
       [Description("Url of the new version of the document you would like compare the original with. Protected SharePoint and OneDive links are supported.")] string newFileUrl,
       [Description("The prompt to compare the document on.")] string comparePrompt,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, originalFileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => $"{z.Filename}\n\n{z.Contents}"));

        var newFiles = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, newFileUrl, cancellationToken);
        var newContents = string.Join("\n\n", newFiles.GetTextFiles().Select(z => $"{z.Filename}\n\n{z.Contents}"));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["firstDocumentContents"] = JsonSerializer.SerializeToElement(contents),
            ["secondDocumentContents"] = JsonSerializer.SerializeToElement(newContents),
            ["prompt"] = JsonSerializer.SerializeToElement(comparePrompt),
        };

        int? progressToken = 1;

        var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var markdown = target.Model;
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-compare", promptArgs, 4096 * 4, "low", 4096, cancellationToken);

                    progressToken = await requestContext.Server.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        markdown,
                        DirectPromptRunner.DocumentModels.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception)
                {

                    return null; // Failure → skip
                }
            });

        var results = await Task.WhenAll(tasks);

        // Return only successful results
        return new MessageResults()
        {
            Results = results.OfType<ProviderMessageResult>()
        };
    }));



}

