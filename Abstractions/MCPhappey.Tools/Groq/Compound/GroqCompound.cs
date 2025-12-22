using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Containers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Groq.Compound;

public static class GroqCompound
{
    [Description("Run a prompt with Groq Compound model.")]
    [McpServerTool(Title = "Groq Compound", Name = "groq_compound_run",
        Destructive = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> GroqCompound_Run(
            IServiceProvider serviceProvider,
          [Description("Prompt to execute.")]
            string prompt,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Target model (e.g. groq/compound or groq/compound-mini).")]
            string model = "groq/compound-mini",
          [Description("Reasoning effort (low, medium of high).")]
            string reasoning = "medium",
          CancellationToken cancellationToken = default)
    {
        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"groq", new {
                    } },
                }),
            Temperature = 0,
            MaxTokens = 8192,
            ModelPreferences = model.ToModelPreferences(),
            Messages = [prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        var metadata = respone.Meta?.ToJsonContent("https://api.groq.com");

        return await requestContext.WithUploads(respone, serviceProvider, metadata, cancellationToken);
    }
}

