using System.ComponentModel;
using System.Text.Json;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenAI.OutlookMail;

public static class OpenAIOutlookMail
{
    private static readonly string[] value = ["list_messages", "search_messages", "get_profile",
            "get_recent_emails", "fetch_message", "fetch_messages_batch"];

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
                                server_label = "outlook_email",
                                authorization = oboToken,
                                connector_id = "connector_outlookemail",
                                require_approval = "never"
                                } }
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

