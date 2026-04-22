using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Maps;

public static class GoogleMapsService
{
    [Description("Run a prompt with Google Maps grounding.")]
    [McpServerTool(Title = "Google Maps",
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> GoogleMaps_Ask(
          [Description("Prompt to execute (code is allowed).")]
            string prompt,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Target model (e.g. gemini-2.5-flash or gemini-2.5-pro).")]
        string model = "gemini-2.5-flash",
          CancellationToken cancellationToken = default)
    {
     var response = await requestContext.Server.SampleAsync(
            new CreateMessageRequestParams()
            {
                Metadata = new JsonObject
                {
                    ["google"] = new JsonObject
                    {
                        ["googleMaps"] = new JsonObject(),
                        ["thinkingConfig"] = new JsonObject
                        {
                            ["thinkingBudget"] = -1
                        }
                    }
                },
                Temperature = 0,
                MaxTokens = 8192,
                ModelPreferences = model.ToModelPreferences(),
                Messages = [prompt.ToUserSamplingMessage()]
            },
            cancellationToken);

        return response.Content;
    }
}

