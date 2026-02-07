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

public static class DocumentTools
{
    private static readonly string[] ModelNames_Actions = [
        "gpt-5-mini",
        "gemini-2.5-flash",
        "claude-haiku-4-5-20251001",
        "grok-4-fast-reasoning"
    ];

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
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var samplingService = serviceProvider.GetRequiredService<SamplingService>();
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

            var tasks = ModelNames_Actions.Select(async modelName =>
            {
                try
                {
                    var startTime = DateTime.UtcNow;

                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-actions",
                        promptArgs,
                        modelName,
                        maxTokens: 4096 * 2,
                        metadata: new Dictionary<string, object>
                        {
                            { "google", new {
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            }},
                            { "openai", new {
                                reasoning = new {
                                    effort = "medium"
                                }
                            }},
                            { "xai", new { reasoning = new {

                                } }},
                            { "anthropic", new {
                                thinking = new {
                                    budget_tokens = 1024
                                }
                            }},
                        },
                        cancellationToken: cancellationToken
                    );

                    var endTime = DateTime.UtcNow;
                    result.Meta?.Add("duration", (endTime - startTime).ToString());

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        modelName,
                        ModelNames_Actions.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception ex)
                {
                    await mcpServer.SendMessageNotificationAsync(
                        $"{modelName} failed: {ex.Message}",
                        LoggingLevel.Error
                    );
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<CreateMessageResult>()
            };
        }));

    private static readonly string[] ModelNames_Glossary = [
        "gpt-5-mini",
        "gemini-2.5-flash-lite",
        "claude-haiku-4-5-20251001",
        "grok-4-fast-reasoning"
    ];

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
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var samplingService = serviceProvider.GetRequiredService<SamplingService>();
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

            var tasks = ModelNames_Glossary.Select(async modelName =>
            {
                try
                {
                    var startTime = DateTime.UtcNow;

                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-glossary",
                        promptArgs,
                        modelName,
                        maxTokens: 4096 * 2,
                        metadata: new Dictionary<string, object>
                        {
                            { "google", new {
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            }},
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            }},
                            { "xai", new { reasoning = new {

                                } }},
                            { "anthropic", new {
                                thinking = new {
                                    budget_tokens = 1024
                                }
                            }},
                        },
                        cancellationToken: cancellationToken
                    );

                    var endTime = DateTime.UtcNow;
                    result.Meta?.Add("duration", (endTime - startTime).ToString());

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        modelName,
                        ModelNames_Glossary.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception ex)
                {
                    await mcpServer.SendMessageNotificationAsync(
                        $"{modelName} failed: {ex.Message}",
                        LoggingLevel.Error
                    );
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<CreateMessageResult>()
            };
        }));

    private static readonly string[] ModelNames_Stakeholders = [
        "gpt-5-mini",
        "gemini-2.5-flash",
        "claude-haiku-4-5-20251001",
        "grok-4-fast-reasoning"
    ];

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
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var mcpServer = requestContext.Server;
            var samplingService = serviceProvider.GetRequiredService<SamplingService>();
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

            var tasks = ModelNames_Stakeholders.Select(async modelName =>
            {
                try
                {
                    var startTime = DateTime.UtcNow;

                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-doc-stakeholders",
                        promptArgs,
                        modelName,
                        maxTokens: 4096 * 2,
                        metadata: new Dictionary<string, object>
                        {
                            { "google", new {
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            }},
                            { "openai", new {
                                reasoning = new {
                                    effort = "medium"
                                }
                            }},
                           { "xai", new { reasoning = new {

                                } }},
                            { "anthropic", new {
                                thinking = new {
                                    budget_tokens = 2048
                                }
                            }},
                        },
                        cancellationToken: cancellationToken
                    );

                    var endTime = DateTime.UtcNow;
                    result.Meta?.Add("duration", (endTime - startTime).ToString());

                    progressToken = await mcpServer.SendProgressNotificationAsync(
                        requestContext,
                        progressToken,
                        modelName,
                        ModelNames_Stakeholders.Length,
                        cancellationToken
                    );

                    return result;
                }
                catch (Exception ex)
                {
                    await mcpServer.SendMessageNotificationAsync(
                        $"{modelName} failed: {ex.Message}",
                        LoggingLevel.Error
                    );
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);

            return new MessageResults
            {
                Results = results.OfType<CreateMessageResult>()
            };
        }));

}

