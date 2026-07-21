using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.OutlookCalendar;

public static class OpenAIOutlookCalendar
{
    [Description("OpenAI Outlook Calendar connector.")]
    [McpServerTool(Title = "OpenAI Outlook Calendar connector", Name = "openai_outlook_calendar",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_OutlookCalendar(
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
                    ServerLabel = "outlook_calendar",
                    Authorization = oboToken,
                    ConnectorId = "connector_outlookcalendar"
                }
            ]
        }, cancellationToken);

        return [responseText.ToTextContentBlock()];
    }
}
