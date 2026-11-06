using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google;

public sealed class GoogleSettings
{
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Separates caller credentials from configured Google credentials for every provider request.</summary>
internal static class GoogleProviderAccess
{
    public const string ApiHost = "generativelanguage.googleapis.com";
    public const string ApiKeyHeader = "x-goog-api-key";
    public const string ApiKeyVariable = "google_api_key";
    public const string BaseUrl = "https://generativelanguage.googleapis.com/";

    public static bool IsDualModeServer(ServerConfig config) => IsDualModeServer(config.Server.ServerInfo.Name);

    private static bool IsDualModeServer(string? name) => name is not null &&
        (name.Equals("Google-Agents", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Google-Audio", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Google-CodeExecution", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Google-NanoBanana", StringComparison.OrdinalIgnoreCase));

    private static bool IsGoogleServer(string? name) => IsDualModeServer(name)
        || string.Equals(name, "Google-AgentEditor", StringComparison.OrdinalIgnoreCase);

    public static ServerConfig? GetConfig(IServiceProvider services, McpServer? server = null)
    {
        var config = server is null ? null : services.GetServerConfig(server)
            ?? throw new InvalidOperationException("Unknown MCP server.");
        if (config is not null && IsGoogleServer(config.Server.ServerInfo.Name))
            return config;

        var context = services.GetService<IHttpContextAccessor>()?.HttpContext;
        if (context?.Items[typeof(ServerRequestAccess)] is ServerRequestAccess access
            && IsGoogleServer(access.Config.Server.ServerInfo.Name))
        {
            if (config is not null)
                throw new UnauthorizedAccessException("Google credentials belong to a different MCP server.");
            return access.Config;
        }

        var requestedServer = context?.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (IsGoogleServer(requestedServer))
            throw new UnauthorizedAccessException("Validated MCP request access is required.");
        return null;
    }

    public static string GetApiKey(IServiceProvider services, McpServer? server = null, ServerConfig? config = null)
    {
        config ??= GetConfig(services, server);
        string? ConfiguredKey() => services.GetService<GoogleSettings>()?.ApiKey;
        if (config is not null)
        {
            if (!IsDualModeServer(config) && services.GetEffectiveAccessMode(config) != ServerAccessMode.Delegated)
                throw new UnauthorizedAccessException("Google Agent Editor requires delegated authentication.");
            return ProviderApiKeyResolver.Resolve(services, config, ApiKeyVariable, ConfiguredKey);
        }

        // Meta servers and direct-prompt consumers retain configured-key behavior. Target MCPs never reach this path.
        var key = ConfiguredKey();
        return !string.IsNullOrWhiteSpace(key) ? key.Trim()
            : throw new InvalidOperationException("Google provider credentials have not been configured.");
    }

    public static bool IsHeaderMode(IServiceProvider services, McpServer server)
        => GetConfig(services, server) is { } config
            && services.GetEffectiveAccessMode(config) == ServerAccessMode.Headers;

    public static void ValidateAgentUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase)
            || !(uri.AbsolutePath.Equals("/v1beta/agents", StringComparison.Ordinal)
                || uri.AbsolutePath.StartsWith("/v1beta/agents/", StringComparison.Ordinal))
            || uri.AbsolutePath.Contains('%') || uri.AbsolutePath.Contains('\\'))
            throw new UnauthorizedAccessException("Google agent credentials are restricted to the HTTPS Google Agents API.");
    }
}
