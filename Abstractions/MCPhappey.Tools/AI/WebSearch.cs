using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class WebSearch
{
    private static readonly string[] ModelNames = ["sonar-pro", "gpt-5.4-mini", "gemini-3-flash-preview",
        "claude-haiku-4-5-20251001", "grok-4-fast-non-reasoning", "mistral-medium-latest", "openai/gpt-oss-20b"];
    private static readonly string[] AcademicModelNames = ["sonar-reasoning-pro", "gpt-5.1",
        "gemini-3.1-pro-preview",
        "claude-opus-4-7", "grok-4-fast-reasoning", "mistral-large-latest"];

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
       CancellationToken cancellationToken = default) =>
       await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var modelName = "gemini-3-flash-preview";

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
                    tools= new object []
                    {
                         new {type = "google_search",
                                    timeRangeFilter = new {
                                        startTime = startDate,
                                        endTime = endDate
                                    },
                                    search_context_size = searchContextSize
                                    },
                        new {type = "google_maps"},
                    },
                    thinkingConfig = new {
                        thinkingBudget = -1
                    }
                }}
            },
            cancellationToken: cancellationToken
        );

        var endTime = DateTime.UtcNow;
        result.Meta?.Add("duration", (endTime - startTime).ToString());

        return result;
    }));


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
                        maxTokens: 10000,
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
                                tools =  new[] {
                                    new {type = "google_search",
                                    timeRangeFilter = new {
                                        startTime = startDate,
                                        endTime = endDate
                                    }}
                                },
                                generation_config = new
                                {
                                     thinking_level = "minimal"
                                }
                            } },
                            { "openai", new {
                                tools =  new[] {
                                    new {type = "web_search"}
                                },
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                            { "mistral", new {
                                 tools =  new[] {
                                    new {type = "web_search_premium"}
                                },
                            } },
                            { "groq", new {
                                  tools =  new[] {
                                    new {type = "browser_search"}
                                }
                            } },
                            { "xai", new {
                                tools =  new[] {
                                    new {type = "web_search"},
                                    new {type = "x_search"}
                                }
                            } },
                            { "anthropic", new {
                                tools = new[] {
                                       new {
                                    type = "web_search_20260209",
                                    name = "web_search",
                                    allowed_callers = new string[]{"direct"},
                                    max_uses = searchContextSize == "low"
                                ? 2 : searchContextSize == "high" ? 6: 7
                                },
                                new {
                                    type = "web_fetch_20260309",
                                    name = "web_fetch",
                                    allowed_callers = new string[]{"direct"},
                                    max_uses = searchContextSize == "low"
                                ? 2 : searchContextSize == "high" ? 6 : 4
                                }},
                                thinking = new {
                                    budget_tokens = 1024,
                                    type = "enabled"
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
        var totalCosts = results.Select(a => a?.GetGatewayCost()).OfType<decimal>().Sum();

        return new CallToolResult()
        {
            StructuredContent = new
            {
                Results = results.OfType<CreateMessageResult>()
            }.ToStructuredContent(),
            Meta = await requestContext.GetToolMeta()
        }
        .WithGatewayCost(totalCosts);
    });


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
                    maxTokens: 10000,
                    metadata: new Dictionary<string, object>
                    {
                    { "perplexity", new {
                        search_mode = "academic",
                        web_search_options = new {
                            search_context_size = searchContextSize
                        }
                     } },
                    { "google", new {
                            tools =  new[] {
                                    new {type = "google_search",
                                    timeRangeFilter = new {
                                        startTime = startDate,
                                        endTime = endDate
                                    }}
                                },
                        thinkingConfig = new {
                            thinkingBudget = -1
                        }
                     } },
                    { "xai", new {
                         tools =  new[] {
                                    new {type = "web_search"}
                                },
                        reasoning = new {
                         }
                    } },
                    { "mistral", new {
                           tools =  new[] {
                                    new {type = "web_search_premium"}
                                }
                    } },
                    { "openai", new {
                        tools =  new[] {
                                    new {type = "web_search"}
                                },
                         reasoning = new {
                            effort = "low"
                         }
                     } },
                    { "anthropic", new {
                         tools = new[] {
                                    new {
                                    type = "web_search_20260209",
                                    name = "web_search",
                                    allowed_callers = new string[]{"direct"},
                                    max_uses = searchContextSize == "low"
                                ? 3 : searchContextSize == "high" ? 7 : 5
                                },
                                new {
                                    type = "web_fetch_20260309",
                                    name = "web_fetch",
                                    allowed_callers = new string[]{"direct"},
                                    max_uses = searchContextSize == "low"
                                ? 3 : searchContextSize == "high" ? 7 : 5
                                }},

                         thinking = new {
                            type = "adaptive"
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

