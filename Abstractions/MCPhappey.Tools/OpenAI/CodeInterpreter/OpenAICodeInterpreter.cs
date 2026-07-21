using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.CodeInterpreter;

public static class OpenAICodeInterpreter
{
    [Description("Run a prompt with the OpenAI Code Interpreter tool.")]
    [McpServerTool(Title = "OpenAI Code Interpreter", Name = "openai_codeinterpreter_run", Destructive = false, ReadOnly = true)]
    public static async Task<CallToolResult?> OpenAICodeInterpreter_Run(
        IServiceProvider serviceProvider,
        [Description("Prompt to execute (code is allowed).")] string prompt,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional OpenAI model override.")] string? model = OpenAIResponsesClient.DefaultModel,
        [Description("Reasoning effort level: low, medium, or high.")] string reasoningEffort = "low",
        [Description("Optional Code Interpreter container id.")] string? containerId = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

            var codeInterpreter = new JsonObject { ["type"] = "code_interpreter" };
            if (!string.IsNullOrWhiteSpace(containerId))
                codeInterpreter["container"] = containerId;

            var response = await serviceProvider.GetRequiredService<OpenAIResponsesClient>().CreateResponseAsync(new JsonObject
            {
                ["model"] = OpenAIResponsesClient.ResolveModel(model),
                ["input"] = prompt,
                ["reasoning"] = new JsonObject { ["effort"] = reasoningEffort },
                ["tools"] = new JsonArray { codeInterpreter }
            }, cancellationToken);

            var blocks = new List<ContentBlock>();
            var outputText = OpenAIResponsesClient.GetOutputText(response);
            if (!string.IsNullOrWhiteSpace(outputText))
                blocks.Add(outputText.ToTextContentBlock());

            blocks.Add(new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents
                {
                    Text = response.ToJsonString(),
                    Uri = "https://api.openai.com/v1/responses",
                    MimeType = MimeTypes.Json
                }
            });

            return blocks.ToCallToolResponse();
        });
}
