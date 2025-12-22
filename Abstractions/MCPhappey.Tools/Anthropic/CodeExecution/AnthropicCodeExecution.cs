using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Anthropic.CodeExecution;

public static class AnthropicCodeExecution
{
    [Description("Run a prompt with Anthropic code execution. Optionally attach files by URL first.")]
    [McpServerTool(Title = "Anthropic Code Execution",
        ReadOnly = true)]
    public static async Task<CallToolResult?> AnthropicCodeExecution_Run(
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Optional file URLs to download and attach before running the prompt.")]
        string[]? fileUrls = null,
          [Description("Target model (e.g. claude-haiku-4-5-20251001 or claude-sonnet-4-5-20250929).")]
        string model = "claude-haiku-4-5-20251001",
          [Description("Max tokens.")]
        int maxTokens = 16384,
          [Description("Optional skills to use. Valid options are: pptx, xlsx, pdf, docx or custom skill ids")]
        string[]? skills = null,
          [Description("Optional container id.")]
        string? containerId = null,
          [Description("Thinking budget.")]
        int? thinkingBudget = 2048,
          CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(prompt);

        var mcpServer = requestContext.Server;
        var samplingService = serviceProvider.GetRequiredService<SamplingService>();
        var downloader = serviceProvider.GetRequiredService<DownloadService>();

        // 1) Download + upload files (optional)
        var attachedLinks = new List<FileItem>();
        if (fileUrls?.Length > 0)
        {
            foreach (var url in fileUrls)
            {
                var data = await downloader.ScrapeContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
                attachedLinks.AddRange(data);
            }

            if (attachedLinks.Count > 0)
            {
                await mcpServer.SendMessageNotificationAsync(
                    $"Attached {attachedLinks.Count} file(s) for code execution.", LoggingLevel.Info, cancellationToken);
            }
        }

        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"anthropic", new {
                        code_execution = new { },
                        thinking = thinkingBudget.HasValue ? new {
                            budget_tokens = thinkingBudget.Value
                        } : null,
                        container = skills?.Any() == true ? new
                        {
                            id = containerId,
                            skills = skills.Select(a => new
                            {
                                skill_id = a,
                                type = a.StartsWith("skill_") ? "custom" : "anthropic",
                                version = "latest"
                            })
                        } : null
                     } },
                }),
            Temperature = 0,
            MaxTokens = maxTokens,
            ModelPreferences = model.ToModelPreferences(),
            Messages = [.. attachedLinks.Select(t => t.Contents.ToString().ToUserSamplingMessage()), prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        var metadata = respone.Meta?.ToJsonContent("https://api.anthropic.com");

        return await requestContext.WithUploads(respone, serviceProvider, metadata, cancellationToken: cancellationToken);
    }
}

