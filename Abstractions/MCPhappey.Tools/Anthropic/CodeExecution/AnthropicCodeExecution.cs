using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Anthropic.Messages;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

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

        var messageContent = new JsonArray();
        foreach (var attachment in attachedLinks)
        {
            var text = attachment.Contents.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                messageContent.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }
        messageContent.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });

        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = messageContent }
            },
            ["tools"] = new JsonArray
            {
                new JsonObject { ["type"] = "code_execution_20250825", ["name"] = "code_execution" }
            }
        };

        // thinking (optioneel)
        if (thinkingBudget.HasValue)
        {
            request["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = thinkingBudget.Value
            };
        }

        // container + skills (optioneel)
        if (skills?.Any() == true)
        {
            request["container"] = new JsonObject
            {
                ["id"] = containerId,
                ["skills"] = new JsonArray(
                    skills.Select(a =>
                        new JsonObject
                        {
                            ["skill_id"] = a,
                            ["type"] = a.StartsWith("skill_") ? "custom" : "anthropic",
                            ["version"] = "latest"
                        }
                    ).ToArray()
                )
            };
        }

        var client = serviceProvider.GetRequiredService<AnthropicMessagesClient>();
        var response = await client.CreateMessageAsync(request,
            [AnthropicMessagesClient.CodeExecutionBeta, AnthropicMessagesClient.FilesBeta], cancellationToken);

        List<ContentBlock> blocks = [];
        var textResult = AnthropicMessagesClient.GetText(response);
        if (!string.IsNullOrWhiteSpace(textResult))
            blocks.Add(textResult.ToTextContentBlock());

        foreach (var fileId in AnthropicMessagesClient.GetGeneratedFileIds(response))
        {
            var file = await client.DownloadFileAsync(fileId, cancellationToken);
            var upload = await requestContext.Server.Upload(serviceProvider,
                file.Filename, BinaryData.FromBytes(file.Data), cancellationToken);
            if (upload is not null) blocks.Add(upload);
        }

        if (response["container"]?["id"]?.GetValue<string>() is { Length: > 0 } id)
            blocks.Add(new JsonObject { ["containerId"] = id, ["model"] = response["model"]?.GetValue<string>() }
                .ToJsonContent(AnthropicHeaders.ApiBaseUrl));

        return blocks.ToCallToolResponse();
    }
}

