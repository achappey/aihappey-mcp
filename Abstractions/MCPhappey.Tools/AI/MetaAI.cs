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

public static class MetaAI
{
    private static readonly string[] ModelNames = ["sonar-pro", "gpt-5-mini", "gemini-2.5-flash", "grok-4-fast-non-reasoning",
            "claude-haiku-4-5-20251001", "mistral-medium-latest"];

    [Description("Ask once, answer from many. Sends the same prompt to multiple AI providers in parallel and returns their answers.")]
    [McpServerTool(
       Title = "Ask (multi-model)",
       Name = "ask_execute",
       ReadOnly = true
   )]
    public static async Task<CallToolResult?> Ask_Execute(
       [Description("User prompt or question")] string prompt,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
    {
        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();

        // Progress + logging
        int? progressToken = 1;
        await mcpServer.SendMessageNotificationAsync(
            $"Meta-AI: {string.Join(", ", ModelNames)}\n{prompt}",
            LoggingLevel.Debug,
            cancellationToken: CancellationToken.None
        );

        // Optional: per-provider generation hints (no web_search here)
        var metadata = new Dictionary<string, object?>
        {
            ["openai"] = new
            {
                reasoning = new
                {
                    effort = "high"
                }
            },
            ["xai"] = new
            {
            },
            ["anthropic"] = new
            {
                thinking = new
                {
                    budget_tokens = 1024
                }
            },
            ["google"] = new
            {
                thinkingConfig = new
                {
                    thinkingBudget = -1
                }
            },
            ["perplexity"] = new
            {
                search_mode = "web",
            },
            ["mistral"] = new
            {

            }
        };

        // Parallel calls per model
        var tasks = ModelNames.Select(async modelName =>
        {
            try
            {
                var startTime = DateTime.UtcNow;
                // Use your general (non-search) prompt id
                var result = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
                {
                    IncludeContext = ContextInclusion.AllServers,
                    MaxTokens = 4096,
                    ModelPreferences = modelName?.ToModelPreferences(),
                    Temperature = 1,
                    Metadata = JsonSerializer.SerializeToElement(metadata),
                    Messages = [prompt.ToUserSamplingMessage()]
                });

                var endTime = DateTime.UtcNow;
                result.Meta?.Add("duration", (endTime - startTime).ToString());

                // Progress tick
                progressToken = await mcpServer.SendProgressNotificationAsync(
                    requestContext,
                    progressToken,
                    $"{modelName} ✓",
                    ModelNames.Length,
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
                return null; // Skip failed
            }
        });

        var results = await Task.WhenAll(tasks);

        return new MessageResults()
        {
            Results = results?.OfType<CreateMessageResult>() ?? []
        };

    });
}

