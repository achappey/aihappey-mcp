using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentHighlighter
{
    [Description("Parallel document highlighter across multiple AI models.")]
    [McpServerTool(Title = "Document highlighter (multi-model)",
        Name = "document_highlighter_summarize",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentHighlighter_Highlight(
       [Description("Url of the document you would like to highlight. Protected SharePoint and OneDive links are supported.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Maximum number of highlights to return.")] int? maxHighlights,
       CancellationToken cancellationToken = default) =>
       await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = PromptArguments.Create(
                    ("documentContents", contents)
                );

        if (maxHighlights.HasValue)
        {
            promptArgs.Add("maxHighlights", JsonSerializer.SerializeToElement(maxHighlights));
        }

        int? progressToken = 1;
       
        var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var markdown = target.Model;
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-highlights", promptArgs, 8192, "low", 1024, cancellationToken);

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

