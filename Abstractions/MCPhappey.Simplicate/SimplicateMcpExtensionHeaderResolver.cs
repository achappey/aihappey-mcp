using System.Collections.Concurrent;
using Azure.Security.KeyVault.Secrets;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPhappey.Simplicate;

public sealed class SimplicateMcpExtensionHeaderResolver(SecretClient? secretClient)
    : IMcpExtensionHeaderResolver
{
    private readonly SecretClient? _secretClient = secretClient;
    private readonly ConcurrentDictionary<string, (string Key, string Secret)> _secretsCache = new();

    public async Task<Dictionary<string, string>?> ResolveHeadersAsync(
        IServiceProvider serviceProvider,
        Server server,
        Dictionary<string, string>? requestHeaders,
        CancellationToken cancellationToken = default)
    {
        var merged = server.McpExtension?.Headers == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(server.McpExtension.Headers, StringComparer.OrdinalIgnoreCase);

        if (!IsSimplicateForwarding(server))
            return merged.Count == 0 ? null : merged;

        var tokenProvider = serviceProvider.GetService<HeaderProvider>();
        var (key, secret) = await TryGetKeySecretAsync(tokenProvider, cancellationToken);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
            return merged.Count == 0 ? null : merged;

        merged[HeaderNames.Authorization] = $"Bearer {key}:{secret}";
        return merged;
    }

    private static bool IsSimplicateForwarding(Server server)
    {
        if (server.BaseMcp?.Equals("mcp.simplicate.com", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (Uri.TryCreate(server.McpExtension?.Url, UriKind.Absolute, out var uri))
        {
            return uri.Host.EndsWith(".mcp.simplicate.com", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.Equals("mcp.simplicate.com", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private async Task<(string? Key, string? Secret)> TryGetKeySecretAsync(
        HeaderProvider? tokenProvider,
        CancellationToken cancellationToken)
    {
        if (tokenProvider?.Headers?.ContainsKey("Authentication-Key") == true &&
            tokenProvider.Headers.TryGetValue("Authentication-Secret", out string? secretFromHeaders))
        {
            return (
                tokenProvider.Headers["Authentication-Key"].ToString(),
                secretFromHeaders.ToString()
            );
        }

        if (string.IsNullOrEmpty(tokenProvider?.Bearer))
            return (null, null);

        var oid = tokenProvider.GetOidClaim();
        if (string.IsNullOrEmpty(oid))
            return (null, null);

        var credentials = await GetCredentialsAsync(oid, cancellationToken);
        return credentials is null
            ? (null, null)
            : (credentials.Value.Key, credentials.Value.Secret);
    }

    private async Task<(string Key, string Secret)?> GetCredentialsAsync(
        string oid,
        CancellationToken cancellationToken)
    {
        if (_secretsCache.TryGetValue(oid, out var cached))
            return cached;

        if (_secretClient == null)
            return null;

        var secret = await _secretClient.GetSecretAsync(oid, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(secret.Value.Properties.ContentType)
            || string.IsNullOrWhiteSpace(secret.Value.Value))
        {
            return null;
        }

        var creds = (secret.Value.Properties.ContentType, secret.Value.Value);
        _secretsCache[oid] = creds;
        return creds;
    }
}

