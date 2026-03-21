using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Olostep;

public sealed class OlostepClient
{
    private const string JsonMimeType = "application/json";
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OlostepClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.olostep.com/");
    }

    public async Task<JsonNode?> PostJsonAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, JsonMimeType)
        };

        return await SendAsync(request, relativePath, cancellationToken);
    }

    public async Task<JsonNode?> GetJsonAsync(string relativePath, IDictionary<string, string?>? query, CancellationToken cancellationToken)
    {
        var path = BuildPath(relativePath, query);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, relativePath, cancellationToken);
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
    {
        using var response = await _client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError(path, response.StatusCode, raw);

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    private static string BuildPath(string relativePath, IDictionary<string, string?>? query)
    {
        var path = relativePath.TrimStart('/');
        if (query is null)
            return path;

        var parts = query
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}")
            .ToArray();

        return parts.Length == 0
            ? path
            : $"{path}?{string.Join("&", parts)}";
    }

    private static Exception BuildError(string path, System.Net.HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var message = node?["message"]?.GetValue<string>()
                ?? node?["detail"]?.GetValue<string>()
                ?? node?["error"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(message))
                return new Exception($"Olostep {path} failed ({(int)statusCode}): {message}");
        }
        catch
        {
            // Ignore parse errors and fall back to the raw response body.
        }

        return new Exception($"Olostep {path} failed ({(int)statusCode}): {raw}");
    }
}

public sealed class OlostepSettings
{
    public string ApiKey { get; set; } = default!;
}
