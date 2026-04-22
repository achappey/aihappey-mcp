using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Mistral.CodeInterpreter;

public static class MistralCodeInterpreter
{
    [Description("Run a prompt with Mistral Code interpreter tool.")]
    [McpServerTool(Title = "Mistral Code Interpreter", Name = "mistral_codeinterpreter_run",
        IconSource = MistralConstants.ICON_SOURCE,
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> MistralCodeInterpreter_Run(
            IServiceProvider serviceProvider,
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Target model (e.g. mistral-large-latest or mistral-medium-latest).")]
            string model = "mistral-medium-latest",
          CancellationToken cancellationToken = default)
    {
        var response = await requestContext.Server.SampleAsync(
            new CreateMessageRequestParams()
            {
                Metadata = new JsonObject
                {
                    ["mistral"] = new JsonObject
                    {
                        ["code_interpreter"] = new JsonObject
                        {
                            ["type"] = "code_interpreter"
                        }
                    }
                },
                Temperature = 0,
                MaxTokens = 8192,
                ModelPreferences = model.ToModelPreferences(),
                Messages = [prompt.ToUserSamplingMessage()]
            },
            cancellationToken);

        var metadata = response.Meta?.ToJsonContent("https://api.mistral.ai");

        return await requestContext.WithUploads(response, serviceProvider, metadata, cancellationToken);
    }
}

