using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.AIsa;

public sealed class AIsaClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AIsaClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<JsonNode?> GetAsync(string path, Dictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        var uri = BuildUri(path, query);
        using var response = await _client.GetAsync(uri, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AIsa GET '{uri}' failed ({(int)response.StatusCode}): {raw}");

        return ParseJson(raw);
    }

    public async Task<JsonNode?> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AIsa POST '{path}' failed ({(int)response.StatusCode}): {raw}");

        return ParseJson(raw);
    }

    private static string BuildUri(string path, Dictionary<string, string?>? query)
    {
        if (query == null || query.Count == 0)
            return path;

        var pairs = query
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
            .ToList();

        if (pairs.Count == 0)
            return path;

        var separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}{string.Join("&", pairs)}";
    }

    private static JsonNode ParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new JsonObject();

        return JsonNode.Parse(raw) ?? new JsonObject { ["content"] = raw };
    }
}

