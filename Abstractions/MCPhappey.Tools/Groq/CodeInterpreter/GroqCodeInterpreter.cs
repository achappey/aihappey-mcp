using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Groq.CodeInterpreter;

public static class GroqCodeInterpreter
{
  [Description("Run a prompt with Groq Code interpreter tool.")]
  [McpServerTool(Title = "Groq Code Interpreter", Name = "groq_codeinterpreter_run",
      Destructive = false,
      ReadOnly = true)]
  public static async Task<CallToolResult?> GroqCodeInterpreter_Run(
          IServiceProvider serviceProvider,
        [Description("Prompt to execute (code is allowed).")]
            string prompt,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target model (e.g. openai/gpt-oss-20b or openai/gpt-oss-120b).")]
            string model = "openai/gpt-oss-20b",
        [Description("Reasoning effort (low, medium of high).")]
            string reasoning = "medium",
        CancellationToken cancellationToken = default)
  {
    var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
    {
      Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"groq", new {
                        code_interpreter = new {
                          type = "code_interpreter",
                          container = new {  type= "auto"} },
                          reasoning = new
                                {
                                      effort = reasoning
                                }
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

