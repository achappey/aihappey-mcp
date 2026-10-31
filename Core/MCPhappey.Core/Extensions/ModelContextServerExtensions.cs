using MCPhappey.Common.Models;
using MCPhappey.Auth;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static partial class ModelContextServerExtensions
{
    public static GradioPlugin ToGradio(this ServerConfig server, HttpContext httpContext, ServerAccessPolicy? accessPolicy = null)
     => new()
     {
         Id = server.Server.ServerInfo.Name,
         Title = server.Server.ServerInfo.Title ?? server.Server.ServerInfo.Name,
         Transport = new()
         {
              Url = server.Server.GetUrl(httpContext),
              Headers = (accessPolicy ?? new()).GetClientHeaders(server.Server)
         }
     };

    public static ServerConfig? GetServerConfig(this IEnumerable<ServerConfig> servers, McpServer mcpServer)
       => servers.GetServerConfig(mcpServer.ServerOptions.ServerInfo?.Name!);

    public static ServerConfig? GetServerConfig(this IEnumerable<ServerConfig> servers, string name)
       => servers.FirstOrDefault(s =>
                  name?.Equals(s.Server.ServerInfo.Name,
                    StringComparison.OrdinalIgnoreCase) == true);

    public static IEnumerable<ServerConfig> WithoutHiddenServers(this IEnumerable<ServerConfig> servers)
        => servers.Where(a => a.Server.Hidden != true);

    public static MCPServerRegistry ToMcpServerRegistry(this IEnumerable<ServerConfig> servers, string baseUrl,
        IReadOnlyList<Microsoft.Graph.Beta.Models.User> users,
        bool sse = false, ServerAccessPolicy? accessPolicy = null)
        => new()
        {
            Servers = servers
                 .WithoutHiddenServers()
                 .Where(server => (accessPolicy ?? new()).GetMode(server.Server) != ServerAccessMode.Unavailable)
                .OrderBy(a => a.Server.ServerInfo.Name)
                 .Select(a => a.ToMCPServerRegistryItem(baseUrl, users, accessPolicy)),
            Metadata = new()
            {
                Count = servers.WithoutHiddenServers().Count(server => (accessPolicy ?? new()).GetMode(server.Server) != ServerAccessMode.Unavailable)
            }
        };

    public static MCPServerList ToMcpServerList(this IEnumerable<ServerConfig> servers, string baseUrl, bool sse = false, ServerAccessPolicy? accessPolicy = null)
        => new()
        {
            Servers = servers
                .WithoutHiddenServers()
                .Where(server => (accessPolicy ?? new()).GetMode(server.Server) != ServerAccessMode.Unavailable)
                .OrderBy(a => a.Server.ServerInfo.Name)
                .ToDictionary(a => a.Server.ServerInfo.Name, a => sse ? a.ToSseMcpServer(baseUrl, accessPolicy)
                        : a.ToMcpServer(baseUrl, accessPolicy))
        };


    public static MCPServerSettingsList ToMcpServerSettingsList(this IEnumerable<ServerConfig> servers, string baseUrl, ServerAccessPolicy? accessPolicy = null)
     => new()
     {
         McpServers = servers
                .WithoutHiddenServers()
                .Where(server => (accessPolicy ?? new()).GetMode(server.Server) != ServerAccessMode.Unavailable)
                .OrderBy(a => a.Server.ServerInfo.Name)
                .ToDictionary(a => a.Server.ServerInfo.Name, a => a.ToMcpServerSettings(baseUrl, accessPolicy))
     };

    public static string WithoutScheme(this string url)
          => new Uri(url).Authority.TrimEnd('/');

    public static string ReverseDns(this string url)
          => string.Join(".", new Uri(url).Host.Split('.').Reverse());

    public static MCPServerRegistryItem ToMCPServerRegistryItem(this ServerConfig server, string baseUrl, IReadOnlyList<Microsoft.Graph.Beta.Models.User> users, ServerAccessPolicy? accessPolicy = null)
               => new()
               {
                   Server = server.ToRegistryServer(baseUrl, accessPolicy),
                   Meta = server.ToMCPServerRegistryItemMeta(baseUrl, users, accessPolicy)
               };
    public static Dictionary<string, Dictionary<string, object>>? ToMCPServerRegistryItemMeta(
        this ServerConfig server,
        string baseUrl,
        IReadOnlyList<Microsoft.Graph.Beta.Models.User> users, ServerAccessPolicy? accessPolicy = null)
    {
        var metadata = new Dictionary<string, Dictionary<string, object>>();
        if (accessPolicy?.GetMode(server.Server) == ServerAccessMode.Delegated)
            metadata["io.aihappey/auth"] = new()
            {
                ["type"] = "oauth2",
                ["provider"] = "microsoft-entra-id",
                ["resourceMetadataUrl"] = $"{baseUrl}/.well-known/oauth-protected-resource/{server.Server.ServerInfo.Name.ToLowerInvariant()}",
                ["tenantId"] = accessPolicy.OAuth!.TenantId,
                ["clientId"] = accessPolicy.OAuth.ClientId,
                ["audience"] = accessPolicy.OAuth.Audience
            };
        if (server.SourceType == ServerSourceType.Static)
            return metadata.Count > 0 ? metadata : null;

        var block = new Dictionary<string, object>
        {
            ["status"] = "active",
            ["isLatest"] = true
        };

        // -------------------------
        // Build owners list
        // -------------------------
        var owners = server.Server.Owners?
            .Select(id =>
            {
                var u = users.FirstOrDefault(x => x.Id == id);
                if (u == null) return null;

                return new Dictionary<string, object>
                {
                    ["name"] = u.DisplayName ?? "",
                    ["email"] = u.Mail ?? ""
                };
            })
            .Where(x => x != null)
            .Cast<Dictionary<string, object>>()
            .ToList()
            ?? [];

        // -------------------------
        // Add owners ONLY if > 0
        // -------------------------
        if (owners.Count > 0)
            block.Add("authors", owners);

        // -------------------------
        // Wrap final metadata
        // -------------------------
        metadata[$"{baseUrl.ReverseDns()}/registry"] = block;
        return metadata;
    }


    public static RegistryServer ToRegistryServer(this ServerConfig server, string baseUrl, ServerAccessPolicy? accessPolicy = null)
            => new()
            {
                Description = server.Server.ServerInfo.Description,
                Version = server.Server.ServerInfo.Version,
                WebsiteUrl = server.Server.ServerInfo.WebsiteUrl,
                Title = server.Server.ServerInfo.Title,
                Icons = server.Server.ServerInfo.Icons?.Select(a => new ServerIcon()
                {
                    Source = a.Source,
                    MimeType = a.MimeType,
                    Sizes = a.Sizes,
                    Theme = a.Theme
                }),
                Name = $"{baseUrl.ReverseDns()}/{server.Server.ServerInfo.Name}",
                Repository = new()
                {
                    Source = "github",
                    Url = "https://github.com/achappey/aihappey-mcp",
                    Subfolder = server.SourceType == ServerSourceType.Static ?
                        $"/tree/main/Servers/MCPhappey.Servers.JSON/Servers/{server.Server.ServerInfo.Name}" :
                        "/tree/main/Servers/MCPhappey.Servers.SQL"
                },
                Remotes = [new ServerRemote() {
                    Url =  $"{baseUrl}/{server.Server.ServerInfo.Name.ToLowerInvariant()}",
                    Headers = (accessPolicy ?? new()).GetPublicHeaders(server.Server)
                }]
            };

    public static MCPServer ToMcpServer(this ServerConfig server, string baseUrl, ServerAccessPolicy? accessPolicy = null)
        => new()
        {
            Type = "http",
            Url = $"{baseUrl}/{server.Server.ServerInfo.Name.ToLowerInvariant()}",
            Headers = (accessPolicy ?? new()).GetClientHeaders(server.Server)
        };

    public static MCPServer ToSseMcpServer(this ServerConfig server, string baseUrl, ServerAccessPolicy? accessPolicy = null)
        => new()
        {
            Type = "sse",
            Url = $"{baseUrl}/{server.Server.ServerInfo.Name.ToLowerInvariant()}/sse",
            Headers = (accessPolicy ?? new()).GetClientHeaders(server.Server)
        };

    public static MCPServerSettings ToMcpServerSettings(this ServerConfig server, string baseUrl, ServerAccessPolicy? accessPolicy = null)
            => new()
            {
                TransportType = "http",
                Url = $"{baseUrl}/{server.Server.ServerInfo.Name.ToLowerInvariant()}",
                Headers = (accessPolicy ?? new()).GetClientHeaders(server.Server)
            };

    public static Implementation ToServerInfo(this Server server)
        => new()
        {
            Name = server.ServerInfo.Name,
            Title = server.ServerInfo.Title,
            Version = server.ServerInfo.Version,
            Icons = server.ServerInfo.Icons?.ToList(),
        };

    public static string GetServerRelativeUrl(this Server server)
        => $"/{server.ServerInfo.Name.ToLowerInvariant()}";


    public static string GetUrl(this Server server, HttpContext httpContext) =>
        $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{server.GetServerRelativeUrl()}";


    public static async Task<ResourceLinkBlock?> Upload(this McpServer mcpServer,
                IServiceProvider serviceProvider,
                string filename,
                BinaryData binaryData,
                CancellationToken cancellationToken = default)
    {
        using var client = await serviceProvider.GetOboGraphClient(mcpServer);

        var sizeInKb = binaryData.Length / 1024.0;
        var markdown = $"Upload {filename} ({sizeInKb:F1} KB)";
       

        return await client.Upload(filename, binaryData, cancellationToken);
    }



}
