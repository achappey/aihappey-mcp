using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SandBase;

/// <summary>Authenticated, server-scoped GET resources. Never fetch arbitrary caller-supplied URLs.</summary>
public sealed class SandBaseScraper : IContentScraper
{
    public bool SupportsHost(ServerConfig serverConfig, string url)
        => serverConfig.Server.ServerInfo.Name is "SandBase-Agents" or "SandBase-Credentials" or "SandBase-Skills"
           && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host.Equals("api.sandbase.ai", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<FileItem>?> GetContentAsync(McpServer server, IServiceProvider services,
        string url, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (uri.UserInfo.Length != 0 || uri.Port != 443 || uri.Fragment.Length != 0 ||
            segments.Length < 2 || segments[0] != "v1") throw new ValidationException("Invalid SandBase resource URL.");
        // Resource URLs are only read when the route is explicitly documented for the matching server.
        var serverName = services.GetServerConfig(server)?.Server.ServerInfo.Name;
        var valid = serverName switch
        {
            "SandBase-Agents" => segments[1] == "agents" && (segments.Length == 2
                || (segments.Length == 3 && segments[2].StartsWith("agent_", StringComparison.Ordinal))
                || (segments.Length == 4 && segments[2].StartsWith("agent_", StringComparison.Ordinal) && segments[3] == "versions")
                || (segments.Length == 5 && segments[2].StartsWith("agent_", StringComparison.Ordinal)
                    && segments[3] == "versions" && segments[4].Length > 0)),
            "SandBase-Credentials" => segments[1] == "credentials" && (segments.Length == 2
                || (segments.Length == 3 && segments[2].StartsWith("sec_", StringComparison.Ordinal))),
            "SandBase-Skills" => segments[1] == "skills" && (segments.Length == 2
                || (segments.Length == 3 && Guid.TryParse(segments[2], out _))),
            _ => false
        };
        if (!valid || segments.Any(s => s.Contains('%') || s is "." or ".."))
            throw new ValidationException("SandBase resource route is not permitted for this server.");
        if (!AllowedQuery(serverName!, segments, uri.Query))
            throw new ValidationException("SandBase resource query is not permitted for this route.");
        var node = await SandBaseClient.SendAsync(services, HttpMethod.Get, "v1/" + string.Join('/', segments.Skip(1)) + uri.Query,
            cancellationToken: cancellationToken);
        if (serverName == "SandBase-Credentials") Redact(node);
        return [MCPhappey.Core.Extensions.StringExtensions.ToJsonFileItem(node.ToJsonString(), url)];
    }

    private static bool AllowedQuery(string name, string[] segments, string query)
    {
        if (query.Length == 0) return true;
        if (query[0] != '?') return false;
        var allowed = name switch
        {
            "SandBase-Agents" when segments.Length == 3 => new[] { "version" },
            "SandBase-Agents" when segments.Length == 4 => new[] { "limit", "page" },
            "SandBase-Skills" when segments.Length == 2 => new[] { "page", "page_size" },
            _ => []
        };
        foreach (var pair in query[1..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !allowed.Contains(parts[0]) || parts[1].Length == 0) return false;
            var value = Uri.UnescapeDataString(parts[1]);
            if (parts[0] == "page" && name == "SandBase-Agents")
            {
                if (value.Length > 1024 || value.Any(char.IsControl)) return false;
            }
            else if (!int.TryParse(value, out var number) || number < 1 ||
                     (parts[0] == "limit" && number > 100) || (parts[0] == "page_size" && number > 1000)) return false;
        }
        return true;
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (key is "value_hint" or "value" or "encrypted_value" or "encrypted_payload") obj.Remove(key);
                else Redact(obj[key]);
            }
        }
        else if (node is JsonArray array) foreach (var item in array) Redact(item);
    }
}
