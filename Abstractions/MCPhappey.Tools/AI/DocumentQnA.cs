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

public static class DocumentQnA
{
    private static readonly string[] ModelNames = ["gpt-5-mini", "gemini-2.5-flash", "claude-haiku-4-5-20251001", "grok-4-fast-reasoning"];
    private static readonly string[] AcademicModelNames = ["gpt-5.1", "gemini-2.5-pro", "claude-opus-4-1-20250805", "grok-4-fast-reasoning"];

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
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(question),
            ["documentContents"] = JsonSerializer.SerializeToElement(contents)
        };

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", ModelNames)}\n{question}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug, cancellationToken: CancellationToken.None);

        var tasks = ModelNames.Select(async modelName =>
            {
                try
                {
                    var markdown = $"{modelName}\n{question}";
                    var startTime = DateTime.UtcNow;
                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-answer",
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
                                    budget_tokens = 2048
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


    [Description("Academic document QnA using multiple AI models in parallel")]
    [McpServerTool(Title = "Academic document QnA (multi-model)",
     Name = "document_qna_ask_academic",
     Destructive = false,
     ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> DocumentQnA_AskAcademic(
    [Description("Url of the document you would like to ask")] string fileUrl,
     [Description("Research question")] string researchQuestion,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default)
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var contents = string.Join("\n\n", files.GetTextFiles().Select(z => z.Contents.ToString()));

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(researchQuestion),
            ["documentContents"] = JsonSerializer.SerializeToElement(contents)
        };

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", AcademicModelNames)}\n{researchQuestion}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug);

        var tasks = AcademicModelNames.Select(async modelName =>
        {
            try
            {
                var markdown = $"{modelName}\n{researchQuestion}";

                var result = await samplingService.GetPromptSample(
                    serviceProvider,
                    mcpServer,
                    "ai-doc-research-answer",
                    promptArgs,
                    modelName,
                    maxTokens: 16384,
                    metadata: new Dictionary<string, object>
                    {
                    { "google", new {
                        thinkingConfig = new {
                            thinkingBudget = -1
                        }
                     } },
                    { "xai", new {
                        reasoning = new {
                         }
                    } },
                    { "openai", new {
                         reasoning = new {
                            effort = "low"
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

                progressToken = await requestContext.Server.SendProgressNotificationAsync(
                    requestContext,
                    progressToken,
                    markdown,
                    AcademicModelNames.Length,
                    cancellationToken
                );

                return result.Content; // Success
            }
            catch (Exception ex)
            {
                await requestContext.Server.SendMessageNotificationAsync(
                    $"{modelName} failed: {ex.Message}",
                    LoggingLevel.Error
                );
                return null; // Skip failed
            }
        });

        var results = await Task.WhenAll(tasks);

        // Only keep successes
        return results.Where(r => r != null)!.SelectMany(a => a!);
    }
}

