using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tools.SPAMhunter;

public static class SPAMhunterService
{
    [Description("Check for spam.")]
    [McpServerTool(Title = "Check for spam",
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> SPAMhunter_Check(
        [Description("IP address of the sender.")]
        string ip,
        [Description("Content to check.")]
        string content,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var sPAMhunterClient = serviceProvider.GetRequiredService<SPAMhunterClient>();

        return await sPAMhunterClient.CheckAsync(ip, content,
            cancellationToken) ?? throw new Exception();
    }));
}

