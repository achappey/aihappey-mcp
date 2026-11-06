using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Google.Agents;

public sealed class GoogleAgentsClient(HttpClient httpClient, IServiceProvider services)
{
    public const string AgentsPath = "v1beta/agents";

    public Task<JsonNode> CreateAsync(JsonObject agent, CancellationToken cancellationToken = default, McpServer? server = null)
        => SendAsync(HttpMethod.Post, GoogleProviderAccess.BaseUrl + AgentsPath, agent, cancellationToken, server);

    public Task<JsonNode> DeleteAsync(string agentId, CancellationToken cancellationToken = default, McpServer? server = null)
        => SendAsync(HttpMethod.Delete, $"{GoogleProviderAccess.BaseUrl}{AgentsPath}/{Uri.EscapeDataString(agentId)}", null, cancellationToken, server);

    internal Task<JsonNode> ReadAsync(string url, ServerConfig config, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, url, null, cancellationToken, config: config);

    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken cancellationToken,
        McpServer? server = null,
        ServerConfig? config = null)
    {
        GoogleProviderAccess.ValidateAgentUrl(path);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(GoogleProviderAccess.ApiKeyHeader, GoogleProviderAccess.GetApiKey(services, server, config));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Google Agents API returned {(int)response.StatusCode} {response.StatusCode}: {ReadError(payload)}",
                null,
                response.StatusCode);

        if (string.IsNullOrWhiteSpace(payload))
            return new JsonObject();

        return JsonNode.Parse(payload)
            ?? throw new ValidationException("Google Agents API returned invalid JSON.");
    }

    private static string ReadError(string payload)
    {
        try
        {
            var root = JsonNode.Parse(payload);
            return root?["error"]?["message"]?.GetValue<string>()
                ?? root?["message"]?.GetValue<string>()
                ?? payload;
        }
        catch (JsonException)
        {
            return payload;
        }
    }
}
