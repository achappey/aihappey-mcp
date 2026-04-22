using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
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
          [Description("Target model (e.g. gpt-5 or gpt-5.4-mini).")]
            string model = "gpt-5.4-mini",
          [Description("Reasoning effort level. low, medium, hard ")]
            string reasoningEffort = "low",
          [Description("Optional container id")]
            string? containerId = null,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
    {
        var openai = new JsonObject
        {
            ["code_interpreter"] = new JsonObject
            {
                ["type"] = "auto"
            },
            ["reasoning"] = new JsonObject
            {
                ["effort"] = reasoningEffort
            }
        };

        // container alleen toevoegen als hij bestaat
        if (!string.IsNullOrEmpty(containerId))
        {
            ((JsonObject)openai["code_interpreter"]!)["container"] = containerId;
        }

        var response = await requestContext.Server.SampleAsync(
            new CreateMessageRequestParams()
            {
                Metadata = new JsonObject
                {
                    ["openai"] = openai
                },
                Temperature = 1,
                MaxTokens = 8192,
                ModelPreferences = model.ToModelPreferences(),
                Messages = [prompt.ToUserSamplingMessage()]
            },
            cancellationToken);

        var metadata = new EmbeddedResourceBlock()
        {
            Resource = new TextResourceContents()
            {
                Text = JsonSerializer.Serialize(response.Meta),
                Uri = "https://api.openai.com",
                MimeType = MimeTypes.Json
            }
        };

        return await requestContext.WithUploads(response, serviceProvider, metadata, cancellationToken);
    });
}

