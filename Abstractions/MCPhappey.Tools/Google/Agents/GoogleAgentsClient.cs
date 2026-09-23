using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Google.Agents;

public sealed class GoogleAgentsClient(HttpClient httpClient)
{
    public const string AgentsPath = "v1beta/agents";

    public Task<JsonNode> CreateAsync(JsonObject agent, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, AgentsPath, agent, cancellationToken);

    public Task<JsonNode> DeleteAsync(string agentId, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Delete, $"{AgentsPath}/{Uri.EscapeDataString(agentId)}", null, cancellationToken);

    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
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
