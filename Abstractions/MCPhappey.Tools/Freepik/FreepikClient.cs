using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Freepik;

public class FreepikClient
{
    private readonly HttpClient _client;

    public FreepikClient(HttpClient client, FreepikSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.freepik.com/");
        if (!_client.DefaultRequestHeaders.Contains("x-freepik-api-key"))
            _client.DefaultRequestHeaders.Add("x-freepik-api-key", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var resp = await _client.PostAsync(path.TrimStart('/'), new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        using var resp = await _client.GetAsync(path.TrimStart('/'), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var resp = await _client.SendAsync(request, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}

public class FreepikSettings
{
    public string ApiKey { get; set; } = default!;
}
