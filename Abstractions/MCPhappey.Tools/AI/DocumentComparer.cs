using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class DocumentComparer
{
    private static readonly string[] ModelNames = ["gpt-5.1", "gemini-2.5-pro", "claude-opus-4-1-20250805", "grok-4-fast-reasoning"];

    [Description("Parallel document comparer across multiple AI models.")]
    [McpServerTool(Title = "Document comparer (multi-model)",
        Name = "document_comparer_compare",
        ReadOnly = true)]
    public static async Task<CallToolResult?> DocumentComparer_Compare(
       [Description("Url of the original document you would like to compare. Protected SharePoint and OneDive links are supported.")] string originalFileUrl,
       [Description("Url of the new version of the document you would like compare the original with. Protected SharePoint and OneDive links are supported.")] string newFileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, originalFileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var newFiles = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, newFileUrl, cancellationToken);
        var newContents = string.Join("\n\n", newFiles.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["oldDocumentContents"] = JsonSerializer.SerializeToElement(contents),
            ["newDocumentContents"] = JsonSerializer.SerializeToElement(newContents),
        };

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", ModelNames)}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug, cancellationToken: CancellationToken.None);

        var tasks = ModelNames.Select(async modelName =>
            {
                try
                {
                    var markdown = $"{modelName}";
                    var startTime = DateTime.UtcNow;
                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-compare",
                        promptArgs,
                        modelName,
                        maxTokens: 4096 * 4,
                        metadata: new Dictionary<string, object>
                        {
                            { "google", new {
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            } },
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                            { "xai", new {
                                reasoning = new {
                                }
                            } },
                            { "anthropic", new {
                                thinking = new {
                                    budget_tokens = 4096
                                }
                            } },
                        },
                        cancellationToken: cancellationToken
                    );

                    var endTime = DateTime.UtcNow;
                    result.Meta?.Add("duration", (endTime - startTime).ToString());

                    progressToken = await requestContext.Server.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        markdown,
                        ModelNames.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception ex)
                {
                    await requestContext.Server.SendMessageNotificationAsync(
                        $"{modelName} failed: {ex.Message}",
                        LoggingLevel.Error
                    );
                    return null; // Failure → skip
                }
            });

        var results = await Task.WhenAll(tasks);

        // Return only successful results
        return new MessageResults()
        {
            Results = results.OfType<CreateMessageResult>()
        };
    }));



}

