using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.CodeInterpreter;

public static class OpenAICodeInterpreter
{
    [Description("Run a prompt with OpenAI Code interpreter tool.")]
    [McpServerTool(Title = "OpenAI Code Interpreter", Name = "openai_codeinterpreter_run",
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAICodeInterpreter_Run(
            IServiceProvider serviceProvider,
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Target model (e.g. gpt-5 or gpt-5-mini).")]
            string model = "gpt-5-mini",
          [Description("Reasoning effort level. low, medium, hard ")]
            string reasoningEffort = "low",
          [Description("Optional container id")]
            string? containerId = null,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
    {
        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"openai", new {
                        code_interpreter = new { type = "auto",
                            container = !string.IsNullOrEmpty(containerId) ? containerId : null },
                        reasoning = new {
                                    effort = reasoningEffort
                                }
                     } },
                }),
            Temperature = 1,
            MaxTokens = 8192,
            ModelPreferences = model.ToModelPreferences(),
            Messages = [prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        var metadata = new EmbeddedResourceBlock()
        {
            Resource = new TextResourceContents()
            {
                Text = JsonSerializer.Serialize(respone.Meta),
                Uri = "https://api.openai.com",
                MimeType = MimeTypes.Json
            }
        };

        return await requestContext.WithUploads(respone, serviceProvider, metadata, cancellationToken);
    });
}

