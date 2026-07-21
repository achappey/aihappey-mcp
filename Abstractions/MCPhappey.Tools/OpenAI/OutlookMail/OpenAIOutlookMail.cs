using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.OpenAI.Responses;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.OutlookMail;

public static class OpenAIOutlookMail
{
    [Description("OpenAI Outlook Email connector.")]
    [McpServerTool(Title = "OpenAI Outlook Email connector", Name = "openai_outlook_email",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_OutlookEmail(
          [Description("Prompt to execute.")]
            string prompt,
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
                    ServerLabel = "outlook_email",
                    Authorization = oboToken,
                    ConnectorId = "connector_outlookemail"
                }
            ]
        }, cancellationToken);

        return [responseText.ToTextContentBlock()];
    }
}

