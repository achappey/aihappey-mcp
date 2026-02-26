using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Supadata;

public sealed class SupadataClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SupadataClient(HttpClient client, SupadataSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.supadata.ai/v1/");
        if (!_client.DefaultRequestHeaders.Contains("x-api-key"))
            _client.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _client.GetAsync(path, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Supadata GET {path} failed ({response.StatusCode}): {raw}");
        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    public async Task<JsonNode?> PostAsync(string path, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Supadata POST {path} failed ({response.StatusCode}): {raw}");
        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }
}
