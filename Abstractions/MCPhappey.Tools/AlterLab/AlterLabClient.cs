using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.AlterLab;

public sealed class AlterLabClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AlterLabClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.alterlab.io/api/v1/");
    }

    public async Task<AlterLabResponse> PostJsonAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var response = await _client.PostAsync(relativePath, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Accepted)
            throw BuildError(relativePath, response.StatusCode, raw);

        return new AlterLabResponse((int)response.StatusCode, string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw));
    }

    public async Task<JsonNode?> GetJsonAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(relativePath, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError(relativePath, response.StatusCode, raw);

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    private static Exception BuildError(string path, HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var detail = node?["detail"]?.GetValue<string>()
                ?? node?["error"]?.GetValue<string>()
                ?? node?["message"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(detail))
                return new Exception($"AlterLab {path} failed ({(int)statusCode}): {detail}");
        }
        catch
        {
            // ignore parse errors and fallback to raw
        }

        return new Exception($"AlterLab {path} failed ({(int)statusCode}): {raw}");
    }
}

public sealed record AlterLabResponse(int StatusCode, JsonNode? Body);
