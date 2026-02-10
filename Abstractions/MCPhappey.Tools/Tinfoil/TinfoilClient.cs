using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Tinfoil;

public class TinfoilClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TinfoilClient(HttpClient client, TinfoilSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://inference.tinfoil.sh/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = form
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

public class TinfoilSettings
{
    public string ApiKey { get; set; } = default!;
}
