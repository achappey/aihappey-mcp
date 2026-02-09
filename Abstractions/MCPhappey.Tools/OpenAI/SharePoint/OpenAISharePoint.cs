using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.SharePoint;

public static class OpenAISharePoint
{
    private static readonly string[] value = ["get_site", "search", "get_profile", "list_recent_documents", "fetch"];

    [Description("OpenAI SharePoint connector.")]
    [McpServerTool(Title = "OpenAI SharePoint connector", Name = "openai_sharepoint",
        Destructive = false,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<IEnumerable<ContentBlock>> OpenAI_SharePoint(
          [Description("Prompt to execute.")]
            string prompt,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

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
                        mcp_list_tools = new[] { new { type = "mcp",
                            server_label = "sharepoint",
                            authorization = oboToken,
                            connector_id = "connector_sharepoint",
                            require_approval = "never"
                        , } }
                     } },
                }),
            Temperature = 1,
            MaxTokens = 8192,
            ModelPreferences = "gpt-5.1".ToModelPreferences(),
            Messages = [prompt.ToUserSamplingMessage()]
        }, cancellationToken);

        return respone.Content;
    }
}

