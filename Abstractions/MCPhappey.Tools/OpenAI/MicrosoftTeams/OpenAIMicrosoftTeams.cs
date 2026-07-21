using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.MicrosoftTeams;

public static class OpenAITeams
{
    [Description("OpenAI Microsoft Teams connector.")]
    [McpServerTool(Title = "OpenAI Microsoft Teams connector", Name = "openai_microsoft_teams",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_MicrosoftTeams(
           [Description("Prompt to execute.")] string prompt,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
          [Description("OpenAI model.")] string modelId = "gpt-5.2",
           CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var oboToken = await serviceProvider.GetOboGraphToken(requestContext.Server);
        var client = serviceProvider.GetRequiredService<OpenAIResponsesClient>();
        var responseText = await client.CreateTextResponseAsync(new OpenAIResponsesRequest
        {
            Model = string.IsNullOrWhiteSpace(modelId) ? "gpt-5.2" : modelId,
            Input = prompt,
            Tools =
            [
                new OpenAIMcpTool
                {
                    ServerLabel = "microsoft_teams",
                    Authorization = oboToken,
                    ConnectorId = "connector_microsoftteams"
                }
            ]
        }, cancellationToken);

        return [responseText.ToTextContentBlock()];
    }
}