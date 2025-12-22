using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
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
        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
                {
                    {"google", new {
                        googleMaps = new { },
                        thinkingConfig = new {
                            thinkingBudget = -1
                        }
                     } },
                }),
            Temperature = 0,
            MaxTokens = 8192,
            ModelPreferences = model.ToModelPreferences(),
            Messages = [prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        return respone.Content;
    }
}

