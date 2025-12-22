using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.MicrosoftTeams;

public static class OpenAITeams
{
    private static readonly string[] value =
        ["search", "fetch", "get_chat_members", "get_profile"];

    [Description("OpenAI Microsoft Teams connector.")]
    [McpServerTool(Title = "OpenAI Microsoft Teams connector", Name = "openai_microsoft_teams",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_MicrosoftTeams(
          [Description("Prompt to execute.")] string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(prompt);

        var oboToken = await serviceProvider.GetOboGraphToken(requestContext.Server);
        var respone = await requestContext.Server.SampleAsync(new CreateMessageRequestParams()
        {
            Metadata = JsonSerializer.SerializeToElement(new Dictionary<string, object>()
            {
                {"openai", new {
                    reasoning = new
                            {
                                effort = "none"
                            },
                    mcp_list_tools = new[] {
                        new {
                            type = "mcp",
                            server_label = "microsoft_teams",
                            authorization = oboToken,
                            connector_id = "connector_microsoftteams",
                            require_approval = "never"
                        }
                    }
                }},
            }),
            Temperature = 1,
            MaxTokens = 8192,
            ModelPreferences = "gpt-5.1".ToModelPreferences(),
            Messages = [prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        return respone.Content;
    }
}
