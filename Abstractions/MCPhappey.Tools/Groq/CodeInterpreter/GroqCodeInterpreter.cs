using System.ComponentModel;
using System.Text.Json.Nodes;
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
    var response = await requestContext.Server.SampleAsync(
       new CreateMessageRequestParams()
       {
         Metadata = new JsonObject
         {
           ["groq"] = new JsonObject
           {
             ["tools"] = new JsonArray
                   {
                    new JsonObject
                    {
                        ["type"] = "code_interpreter",
                        ["container"] = new JsonObject
                        {
                            ["type"] = "auto"
                        }
                    }
                   },
             ["reasoning"] = new JsonObject
             {
               ["effort"] = reasoning
             }
           }
         },
         Temperature = 0,
         MaxTokens = 8192,
         ModelPreferences = model.ToModelPreferences(),
         Messages = [prompt.ToUserSamplingMessage()]
       },
       cancellationToken);

    var metadata = response.Meta?.ToJsonContent("https://api.groq.com");

    return await requestContext.WithUploads(response, serviceProvider, metadata, cancellationToken);
  }
}

