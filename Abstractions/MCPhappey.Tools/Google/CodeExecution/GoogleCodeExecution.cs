using System.ComponentModel;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Google.Interactions;
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
          [Description("Target model (e.g. gemini-flash-latest, gemini-3.6-flash or gemini-3.1-pro-preview).")]
        string model = "gemini-flash-latest",
          CancellationToken cancellationToken = default)
    {
        var interactions = serviceProvider.GetRequiredService<GoogleInteractionsClient>();
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
        }

        var input = new System.Text.Json.Nodes.JsonArray();
        foreach (var item in attachedLinks)
        {
            input.Add(GoogleInteractionInput.Bytes(
                item.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ? "image" : "document",
                item.Contents,
                item.MimeType ?? "application/octet-stream"));
        }
        input.Add(GoogleInteractionInput.Text(prompt));

        var response = await interactions.CreateInteractionAsync(new GoogleInteractionRequest
        {
            Model = model,
            Input = input,
            Tools = [new System.Text.Json.Nodes.JsonObject { ["type"] = "code_execution" }],
            GenerationConfig = new System.Text.Json.Nodes.JsonObject
            {
                ["max_output_tokens"] = 8192,
                ["thinking_level"] = "high"
            }
        }, cancellationToken);

        return await response.ToToolResultAsync(requestContext, serviceProvider, cancellationToken);
    }
}

