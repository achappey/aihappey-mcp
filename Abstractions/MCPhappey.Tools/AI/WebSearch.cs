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

public static class WebSearch
{
    private static readonly string[] ModelNames = ["sonar-pro", "gpt-5-mini", "gemini-2.5-flash",
        "claude-haiku-4-5-20251001", "grok-4-fast-non-reasoning", "mistral-medium-latest", "openai/gpt-oss-20b"];
    private static readonly string[] AcademicModelNames = ["sonar-reasoning-pro", "gpt-5.1", "gemini-2.5-pro",
        "claude-opus-4-1-20250805", "grok-4-fast-reasoning", "mistral-large-latest"];

    [Description("Perform a quick web search using Google AI with Google Search grounding.")]
    [McpServerTool(
        Title = "Google web search",
        Name = "web_search_google",
        ReadOnly = true)]
    public static async Task<CallToolResult?> WebSearch_Google(
       [Description("Search query")] string query,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Start date of the date range")] string? startDate = null,
       [Description("End date of the date range")] string? endDate = null,
       [Description("Search context size. low, medium or high")] string? searchContextSize = "medium",
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var modelName = "gemini-2.5-flash";

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(query)
        };

        await requestContext.Server.SendMessageNotificationAsync(
            $"Google AI search: {query}",
            LoggingLevel.Debug,
            cancellationToken: CancellationToken.None);

        var startTime = DateTime.UtcNow;

        var result = await samplingService.GetPromptSample(
            serviceProvider,
            mcpServer,
            "ai-websearch-answer",
            promptArgs,
            modelName,
            metadata: new Dictionary<string, object>
            {
                { "google", new {
                    google_search = new {
                        timeRangeFilter = new {
                            startTime = startDate,
                            endTime = endDate
                        },
                        search_context_size = searchContextSize
                    },
                    googleMaps = new { },
                    thinkingConfig = new {
                        thinkingBudget = -1
                    }
                }}
            },
            cancellationToken: cancellationToken
        );

        var endTime = DateTime.UtcNow;
        result.Meta?.Add("duration", (endTime - startTime).ToString());

        return result
            .ToJsonContentBlock("https://www.google.com")
            .ToCallToolResult();
    });


    [Description("Parallel web search across multiple AI models, optionally filtered by date range. If a date range is used, include it in the prompt, as some providers don’t support date filters.")]
    [McpServerTool(Title = "Web search (multi-model)",
        Name = "web_search_execute",
        OpenWorld = true,
        Destructive = false,
        Idempotent = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> WebSearch_Execute(
       [Description("Search query")] string query,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       [Description("Start date of the date range")] string? startDate = null,
       [Description("End date of the date range")] string? endDate = null,
       [Description("Search context size. low, medium or high")] string? searchContextSize = "medium",
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(query)
        };

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", ModelNames)}\n{query}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug, cancellationToken: CancellationToken.None);

        var tasks = ModelNames.Select(async modelName =>
            {
                try
                {
                    var markdown = $"{modelName}\n{query}";
                    var startTime = DateTime.UtcNow;
                    var result = await samplingService.GetPromptSample(
                        serviceProvider,
                        mcpServer,
                        "ai-websearch-answer",
                        promptArgs,
                        modelName,
                        metadata: new Dictionary<string, object>
                        {
                            { "perplexity", new {
                                search_mode = "web",
                                web_search_options = new {
                                    search_context_size = searchContextSize
                                },
                                last_updated_before_filter = endDate,
                                last_updated_after_filter = startDate
                            } },
                            { "google", new {
                                google_search = new { timeRangeFilter = new {
                                    startTime = startDate,
                                    endTime = endDate
                                } },
                                thinkingConfig = new {
                                    thinkingBudget = -1
                                }
                            } },
                            { "openai", new {
                                web_search = new {
                                    search_context_size = searchContextSize
                                },
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                            { "mistral", new {
                                web_search_premium = new {
                                    type = "web_search_premium"
                                }
                            } },
                            { "groq", new {
                                browser_search = new {
                                    type = "browser_search"
                                }
                            } },
                            { "xai", new {
                                web_search = new {
                                },
                                x_search = new {
                                }
                            } },
                            { "anthropic", new {
                                web_search = new {
                                    max_uses = searchContextSize == "low" ? 2 : searchContextSize == "high" ? 6 : 4
                                },
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


    [Description("Academic web search using multiple AI models in parallel")]
    [McpServerTool(Title = "Academic web search (multi-model)",
     Destructive = false,
     OpenWorld = true,
     Idempotent = false,
     ReadOnly = true)]
    public static async Task<CallToolResult?> WebSearch_ExecuteAcademic(
     [Description("Search query")] string query,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
       [Description("Start date of the date range")] string? startDate = null,
       [Description("End date of the date range")] string? endDate = null,
       [Description("Search context size. low, medium or high")] string? searchContextSize = "medium",
     CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        var promptArgs = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(query)
        };

        int? progressToken = 1;

        var markdown = $"{string.Join(", ", AcademicModelNames)}\n{query}";
        await requestContext.Server.SendMessageNotificationAsync(markdown, LoggingLevel.Debug);

        var tasks = AcademicModelNames.Select(async modelName =>
        {
            try
            {
                var markdown = $"{modelName}\n{query}";

                var result = await samplingService.GetPromptSample(
                    serviceProvider,
                    mcpServer,
                    "ai-academic-research-answer",
                    promptArgs,
                    modelName,
                    metadata: new Dictionary<string, object>
                    {
                    { "perplexity", new {
                        search_mode = "academic",
                        web_search_options = new {
                            search_context_size = searchContextSize
                        }
                     } },
                    { "google", new {
                        google_search = new {
                            timeRangeFilter = new {
                                    startTime = startDate,
                                    endTime = endDate
                                } },
                        thinkingConfig = new {
                            thinkingBudget = -1
                        }
                     } },
                    { "xai", new {
                        web_search = new {
                        },
                        reasoning = new {
                         }
                    } },
                    { "mistral", new {
                                web_search_premium = new {
                                    type = "web_search_premium"
                                }
                    } },
                    { "openai", new {
                        web_search = new {
                            search_context_size = searchContextSize
                         },
                         reasoning = new {
                            effort = "low"
                         }
                     } },
                    { "anthropic", new {
                        web_search = new {
                              max_uses = searchContextSize == "low"
                                ? 3 : searchContextSize == "high" ? 7 : 5
                         },
                         thinking = new {
                            budget_tokens = 2048
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

                return result; // Success
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

        // Return only successful results
        return new MessageResults()
        {
            Results = results.Where(r => r != null)!.OfType<CreateMessageResult>() ?? []
        };
    }));
}

