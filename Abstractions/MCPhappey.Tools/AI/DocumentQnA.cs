using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentQnA
{
    [Description("Parallel document qna across multiple AI models.")]
    [McpServerTool(Title = "Document QnA (multi-model)",
        Name = "document_qna_ask",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentQnA_Ask(
       [Description("Url of the document you would like to ask. Protected SharePoint and OneDive links are supported.")] string fileUrl,
       [Description("Question prompt about the document")] string question,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(question),
            ["documentContents"] = JsonSerializer.SerializeToElement(contents)
        };

        int? progressToken = 1;

        var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var markdown = $"{target.Model}\n{question}";
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-answer", promptArgs, 8192, "low", 2048, cancellationToken);

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


    [Description("Academic document QnA using multiple AI models in parallel")]
    [McpServerTool(Title = "Academic document QnA (multi-model)",
     Name = "document_qna_ask_academic",
     Destructive = false,
     ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentQnA_AskAcademic(
    [Description("Url of the document you would like to ask")] string fileUrl,
     [Description("Research question")] string researchQuestion,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default) =>
       await ModelContextToolExtensions.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(researchQuestion),
            ["documentContents"] = JsonSerializer.SerializeToElement(contents)
        };

        int? progressToken = 1;

        var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
        {
            try
            {
                var markdown = $"{target.Model}\n{researchQuestion}";
                var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                    "ai-doc-research-answer", promptArgs, 16384, "low", 4096, cancellationToken);

                progressToken = await requestContext.Server.SendProgressNotificationAsync(
                    requestContext,
                    progressToken,
                    markdown,
                    DirectPromptRunner.DocumentModels.Length,
                    cancellationToken
                );

                return result; // Success
            }
            catch (Exception)
            {
                return null; // Skip failed
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

