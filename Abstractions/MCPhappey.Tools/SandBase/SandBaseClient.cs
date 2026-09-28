using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.SandBase;

public sealed class SandBaseSettings
{
    public required string ApiKey { get; init; }
}

internal static class SandBaseClient
{
    internal const string BaseUrl = "https://api.sandbase.ai/v1/";

    internal static async Task<JsonNode> SendAsync(IServiceProvider services, HttpMethod method,
        string path, JsonNode? body = null, CancellationToken cancellationToken = default,
        HttpContent? content = null)
    {
        if (!path.StartsWith("v1/", StringComparison.Ordinal) || path.Contains('#'))
            throw new ValidationException("Invalid SandBase API path.");
        var key = services.GetRequiredService<SandBaseSettings>().ApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("SandBase API key is not configured.");
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient();
        using var request = new HttpRequestMessage(method, "https://api.sandbase.ai/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = content ?? (body is null ? null : JsonContent.Create(body));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"SandBase API returned {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)
            ?? throw new ValidationException("SandBase returned invalid JSON.");
    }

    internal static JsonObject Object(string? json, string field)
    {
        try { return JsonNode.Parse(json ?? "") as JsonObject ?? throw new ValidationException($"{field} must be a JSON object."); }
        catch (System.Text.Json.JsonException) { throw new ValidationException($"{field} must be a JSON object."); }
    }

    internal static JsonArray Array(string? json, string field)
    {
        try { return JsonNode.Parse(json ?? "") as JsonArray ?? throw new ValidationException($"{field} must be a JSON array."); }
        catch (System.Text.Json.JsonException) { throw new ValidationException($"{field} must be a JSON array."); }
    }

    internal static string Required(string? value, string field)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ValidationException($"{field} is required.");

    internal static string AgentId(string value)
    {
        var id = Required(value, "agentId");
        return id.StartsWith("agent_", StringComparison.Ordinal) ? Uri.EscapeDataString(id)
            : throw new ValidationException("agentId must begin with agent_.");
    }

    internal static string SkillId(string value)
        => Guid.TryParse(value, out var id) ? id.ToString() : throw new ValidationException("skillId must be a UUID.");

    internal static string CredentialId(string value)
    {
        var id = Required(value, "credentialId");
        return id.StartsWith("sec_", StringComparison.Ordinal) ? Uri.EscapeDataString(id)
            : throw new ValidationException("credentialId must begin with sec_.");
    }

    internal static string SafeUrl(string value, string host)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && uri.UserInfo.Length == 0
           ? value : throw new ValidationException($"URL must be HTTPS on {host}.");
}
