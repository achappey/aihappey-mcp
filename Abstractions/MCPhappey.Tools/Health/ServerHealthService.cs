using System.ComponentModel;
using System.Diagnostics;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Health;

public static class ServerHealthService
{
    [Description("Return server health diagnostics (status, environment, CPU, boot time, uptime).")]
    [McpServerTool(Title = "Server health", Name = "server_health", ReadOnly = true, Idempotent = true)]
    public static async Task<CallToolResult?> ServerHealth_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithStructuredContent(async () =>
        {
            var env = serviceProvider.GetService<IHostEnvironment>();
            var cpu = serviceProvider.GetRequiredService<CpuUsageTracker>();
            var bootup = Process.GetCurrentProcess().StartTime.ToUniversalTime();

            return new
            {
                status = "ok",
                environment = env?.EnvironmentName ?? "unknown",
                cpuPercent = Math.Round(cpu.GetCpuUsagePercent(), 2),
                bootTime = bootup,
                uptimeSeconds = (int)(DateTime.UtcNow - bootup).TotalSeconds,
            };
        });
}
