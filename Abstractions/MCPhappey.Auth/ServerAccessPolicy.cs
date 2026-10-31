using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using MCPhappey.Auth.Models;
using MCPhappey.Common.Models;

namespace MCPhappey.Auth;

public enum ServerAccessMode { Public, Headers, Delegated, Unavailable }

/// <summary>One policy for transport enforcement, credential use, and discovery. OBO metadata is never mutated.</summary>
public sealed class ServerAccessPolicy
{
    public OAuthSettings? OAuth { get; }

    public ServerAccessPolicy(OAuthSettings? oauth = null)
    {
        if (oauth is not null && (string.IsNullOrWhiteSpace(oauth.TenantId)
            || string.IsNullOrWhiteSpace(oauth.ClientId) || string.IsNullOrWhiteSpace(oauth.ClientSecret)
            || string.IsNullOrWhiteSpace(oauth.Audience) || string.IsNullOrWhiteSpace(oauth.Scopes)))
            throw new InvalidOperationException("OAuth is configured incompletely. Refusing to downgrade delegated access to provider-key access.");
        OAuth = oauth;
    }

    public ServerAccessMode GetMode(Server server)
    {
        var identityRestricted = server.Roles?.Any() == true || server.Groups?.Any() == true;
        if (OAuth is not null && (server.OBO?.Count > 0 || identityRestricted))
            return ServerAccessMode.Delegated;
        if (identityRestricted || (server.OBO?.Count > 0 && server.Owners?.Any() == true))
            return ServerAccessMode.Unavailable;
        if (server.OBO?.Count > 0 && server.Headers?.Count is not > 0)
            return ServerAccessMode.Unavailable;
        return server.Headers?.Count > 0 ? ServerAccessMode.Headers : ServerAccessMode.Public;
    }

    public List<ServerHeader>? GetPublicHeaders(Server server)
    {
        if (GetMode(server) == ServerAccessMode.Delegated)
            return [new ServerHeader
            {
                Name = "Authorization", Value = "Bearer {access_token}", IsRequired = true,
                Description = "OAuth access token obtained through this server's authorization flow (Microsoft Entra ID).",
                Variables = new() { ["access_token"] = new ServerInput
                {
                    Description = "Sign in using OAuth discovery; do not supply a provider API key.",
                    IsRequired = true, IsSecret = true, Format = "string"
                }}
            }];
        if (GetMode(server) == ServerAccessMode.Unavailable)
            return null;
        return server.Headers?.Select(header =>
        {
            var copy = JsonSerializer.Deserialize<ServerHeader>(JsonSerializer.Serialize(header))!;
            var credential = header.IsSecret == true || header.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);
            if (credential)
            {
                copy.Default = null;
                // Only templates are safe to publish for secret inputs, never populated credentials.
                if (copy.Variables?.Count is not > 0)
                    copy.Value = null;
            }
            foreach (var input in copy.Variables?.Values ?? Enumerable.Empty<ServerInput>())
                if (input.IsSecret == true)
                {
                    input.Value = null;
                    input.Default = null;
                }
            return copy;
        }).ToList();
    }

    public Dictionary<string, string>? GetClientHeaders(Server server)
        => GetPublicHeaders(server)?.ToDictionary(header => header.Name,
            header => header.Value ?? $"{{{header.Name}}}",
            StringComparer.OrdinalIgnoreCase);

    public bool HasRequiredHeaders(Server server, IReadOnlyDictionary<string, string> headers)
        => (server.Headers ?? []).Where(header => header.IsRequired == true).All(header =>
            headers.Any(pair => pair.Key.Equals(header.Name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value) && !pair.Value.Contains('{') && !pair.Value.Contains('[')));

    public string GetCredential(Server server, IReadOnlyDictionary<string, string> headers, string variableName)
    {
        if (GetMode(server) != ServerAccessMode.Headers)
            throw new UnauthorizedAccessException("Direct provider credentials are not enabled for this request.");
        if (!HasRequiredHeaders(server, headers))
            throw new UnauthorizedAccessException("Required provider headers are missing.");
        foreach (var header in server.Headers ?? [])
        {
            var incoming = headers.FirstOrDefault(pair => pair.Key.Equals(header.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(incoming) || incoming.Length > 16384)
                continue;
            string? value = null;
            if (header.Value is null && header.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                value = incoming;
            else if (header.Value is { } template && header.Variables?.ContainsKey(variableName) == true)
            {
                var expression = new StringBuilder("\\A");
                var position = 0;
                var targetGroup = "";
                var index = 0;
                foreach (Match placeholder in Regex.Matches(template, "\\{([^{}]+)\\}"))
                {
                    expression.Append(Regex.Escape(template[position..placeholder.Index]));
                    var group = $"v{index++}";
                    expression.Append($"(?<{group}>[^\\s{{}}\\[\\]]+)");
                    if (placeholder.Groups[1].Value == variableName)
                        targetGroup = group;
                    position = placeholder.Index + placeholder.Length;
                }
                expression.Append(Regex.Escape(template[position..])).Append("\\z");
                var match = Regex.Match(incoming, expression.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (match.Success && targetGroup.Length > 0)
                    value = match.Groups[targetGroup].Value;
            }
            if (!string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace) && value.IndexOfAny(['{', '}', '[', ']']) < 0)
                return value;
        }
        throw new UnauthorizedAccessException("The declared provider credential input is missing or invalid.");
    }
}
