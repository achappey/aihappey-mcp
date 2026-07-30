using System.ComponentModel;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class ChatApp
{


    [Description("Get available completions that can be used in prompts and can be completed during using those prompts.")]
    [McpServerTool(Title = "Get prompt completions",
        ReadOnly = true)]
    public static async Task<CallToolResult> ChatApp_GetCompletions(
       IServiceProvider serviceProvider)
    {
        var config = serviceProvider.GetServices<IAutoCompletion>();

        return await Task.FromResult(string.Join(",", config.SelectMany(z => z.GetArguments(serviceProvider))).ToTextCallToolResponse());
    }
}

