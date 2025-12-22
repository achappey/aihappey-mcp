using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.CodeExecution;

public static class GoogleCodeExecution
{
    [Description("Run a prompt with Google code execution. Optionally attach files by URL first.")]
    [McpServerTool(Title = "Google Code Execution",
        ReadOnly = true)]
    public static async Task<CallToolResult?> GoogleCodeExecution_Run(
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Optional file URLs to download and attach before running the prompt.")]
        string[]? fileUrls = null,
          [Description("Target model (e.g. gemini-2.5-flash or gemini-2.5-pro).")]
        string model = "gemini-2.5-flash",
          CancellationToken cancellationToken = default)
    {
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
                    {"google", new {
                        code_execution = new { },
                        thinkingConfig = new {
                            thinkingBudget = -1
                        }
                     } },
                }),
            Temperature = 0,
            MaxTokens = 8192,
            ModelPreferences = model.ToModelPreferences(),
            Messages = [.. attachedLinks.Select(t => t.Contents.ToString().ToUserSamplingMessage()), prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        var metadata = respone.Meta?.ToJsonContent("https://generativelanguage.googleapis.com");

        return await requestContext.WithUploads(respone, serviceProvider, metadata, cancellationToken);
    }
}

