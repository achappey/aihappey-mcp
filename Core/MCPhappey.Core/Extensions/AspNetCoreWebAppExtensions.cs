
using MCPhappey.Common.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;

namespace MCPhappey.Core.Extensions;

public static class AspNetCoreWebAppExtensions
{
    public static void AddMcpServer(
              this WebApplication webApp,
              ServerConfig server)
    {
        var prefix = server.Server.GetServerRelativeUrl();
        var mcpGroup = webApp.MapMcp(prefix);

        // ServerAccessMiddleware enforces the effective policy before every transport request.
    }

    public static WebApplication UseMcpWebApplication(
            this WebApplication webApp,
            List<ServerConfig> servers)
    {
        var policy = webApp.Services.GetRequiredService<ServerAccessPolicy>();
        foreach (var config in servers)
        {
            ServerHeadersJsonConverter.Validate(config.Server.Headers ?? []);
        }
        webApp.UseMiddleware<ServerAccessMiddleware>();

        webApp.MapGet("/v0.1/servers", (HttpContext context,  [FromServices] IReadOnlyList<Microsoft.Graph.Beta.Models.User>? users = null)
                   => Results.Json(servers.ToMcpServerRegistry($"{context.Request.Scheme}://{context.Request.Host}", users ?? [], accessPolicy: policy)));

        webApp.MapGet("/mcp.json", (HttpContext context)
                   => Results.Json(servers.ToMcpServerList($"{context.Request.Scheme}://{context.Request.Host}", accessPolicy: policy)));

        webApp.MapGet("/mcp_sse.json", (HttpContext context)
                   => Results.Json(servers.ToMcpServerList($"{context.Request.Scheme}://{context.Request.Host}",
                      sse: true, accessPolicy: policy)));

        webApp.MapGet("/mcp_settings.json", (HttpContext context)
                   => Results.Json(servers.ToMcpServerSettingsList($"{context.Request.Scheme}://{context.Request.Host}", policy)));

        webApp.MapGet("/gradio.json", (HttpContext ctw)
                   => Results.Json(servers
                    .WithoutHiddenServers()
                    .Where(server => policy.GetMode(server.Server) != ServerAccessMode.Unavailable)
                    .Select(a => a.ToGradio(ctw, policy))));

        foreach (var server in servers)
        {
            if (policy.GetMode(server.Server) == ServerAccessMode.Delegated)
                webApp.MapServerOAuth(server.Server.GetServerRelativeUrl(), policy.OAuth!.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            webApp.AddMcpServer(server);
        }

        return webApp;
    }
}
