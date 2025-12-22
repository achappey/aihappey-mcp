using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.xAI;

public static class GrokCodeExecution
{
    [Description("Run a prompt with xAI Grok code execution. Optionally attach files by URL first.")]
    [McpServerTool(Title = "xAI Grok Code Execution",
        Name = "xai_code_execution_run",
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> XAICodeExecution_Run(
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Optional file URLs to download and attach before running the prompt.")]
            string[]? fileUrls = null,
          CancellationToken cancellationToken = default)
    {
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
                await requestContext.Server.SendMessageNotificationAsync(
                    $"Attached {attachedLinks.Count} file(s) for code execution.", LoggingLevel.Info, cancellationToken);
            }
        }

        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"xai", new {
                        code_execution = new { }
                     } },
                }),
            Temperature = 0,
            MaxTokens = 8192,
            ModelPreferences = "grok-4-fast-reasoning".ToModelPreferences(),
            Messages = [.. attachedLinks.Select(t => t.Contents.ToString()?.ToUserSamplingMessage()!), prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        return respone.Content;
    }
}

