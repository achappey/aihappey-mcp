using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentTools
{
    [Description("Extract action items from the document using multiple AI models in parallel.")]
    [McpServerTool(
        Title = "Document actions (multi-model)",
        Name = "document_tools_actions",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentTools_Actions(
        [Description("Url of the document for extracting action items. Protected SharePoint and OneDrive links are supported.")]
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(
                serviceProvider,
                mcpServer,
                fileUrl,
                cancellationToken);

            var contents = string.Join("\n\n",
                files.GetTextFiles()
                     .Select(z => z.Contents.ToString()));

            var promptArgs = new Dictionary<string, JsonElement>
            {
                ["documentContents"] = JsonSerializer.SerializeToElement(contents)
            };

            int? progressToken = 1;

            var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-actions", promptArgs, 8192, "medium", 1024, cancellationToken);

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        target.Model,
                        DirectPromptRunner.DocumentModels.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception)
                {
                   
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<ProviderMessageResult>()
            };
        }));

    [Description("Extract glossary terms from the document using multiple AI models in parallel.")]
    [McpServerTool(
        Title = "Document glossary (multi-model)",
        Name = "document_tools_glossary",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentTools_Glossary(
        [Description("Url of the document for extracting glossary terms. Protected SharePoint and OneDrive links are supported.")]
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var files = await downloadService.ScrapeContentAsync(
                serviceProvider,
                mcpServer,
                fileUrl,
                cancellationToken);

            var contents = string.Join("\n\n",
                files.GetTextFiles()
                     .Select(z => z.Contents.ToString()));

            var promptArgs = PromptArguments.Create(
                ("documentContents", contents));

            int? progressToken = 1;

            var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-glossary", promptArgs, 8192, "low", 1024, cancellationToken);

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        target.Model,
                        DirectPromptRunner.DocumentModels.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception)
                {
                
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<ProviderMessageResult>()
            };
        }));

    [Description("Extract stakeholders from the document using multiple AI models in parallel.")]
    [McpServerTool(
        Title = "Document stakeholders (multi-model)",
        Name = "document_tools_stakeholders",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentTools_Stakeholders(
        [Description("Url of the document for extracting stakeholders. Protected SharePoint and OneDrive links are supported.")]
        string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var files = await downloadService.ScrapeContentAsync(
                serviceProvider,
                mcpServer,
                fileUrl,
                cancellationToken);

            var contents = string.Join("\n\n",
                files.GetTextFiles()
                     .Select(z => z.Contents.ToString()));

            var promptArgs = new Dictionary<string, JsonElement>
            {
                ["documentContents"] = JsonSerializer.SerializeToElement(contents)
            };

            int? progressToken = 1;

            var tasks = DirectPromptRunner.DocumentModels.Select(async target =>
            {
                try
                {
                    var result = await DirectPromptRunner.RunAsync(serviceProvider, mcpServer, target.Provider,
                        "ai-doc-stakeholders", promptArgs, 8192, "medium", 2048, cancellationToken);

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        target.Model,
                        DirectPromptRunner.DocumentModels.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception)
                {
                   
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<ProviderMessageResult>()
            };
        }));

}

