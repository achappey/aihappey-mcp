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

public static class DocumentHighlighter
{
    private static readonly string[] ModelNames = ["gpt-5-mini", "gemini-2.5-flash", "claude-haiku-4-5-20251001", "grok-4-fast-reasoning"];

    [Description("Parallel document highlighter across multiple AI models.")]
    [McpServerTool(Title = "Document highlighter (multi-model)",
        Name = "document_highlighter_summarize",
        ReadOnly = true)]
    public static async Task<CallToolResult> DocumentHighlighter_Highlight(
       [Description("Url of the document you would like to highlight. Protected SharePoint and OneDive links are supported.")] string fileUrl,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Maximum number of highlights to return.")] int? maxHighlights,
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
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
                        "ai-doc-highlights",
                        promptArgs,
                        modelName,
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
                                    budget_tokens = 1024
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

