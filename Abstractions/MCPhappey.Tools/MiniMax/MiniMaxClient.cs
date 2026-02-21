using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.MiniMax;

public class MiniMaxClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };

    public MiniMaxClient(HttpClient client, MiniMaxSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.minimax.io/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, MimeTypes.Json);

        using var resp = await _client.SendAsync(request, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonDocument.Parse(text);
    }

    public async Task<JsonDocument> GetAsync(string path, Dictionary<string, string?> query, CancellationToken ct)
    {
        var queryString = string.Join("&", query
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        var uri = string.IsNullOrWhiteSpace(queryString) ? path : $"{path}?{queryString}";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var resp = await _client.SendAsync(request, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonDocument.Parse(text);
    }
}

public class MiniMaxSettings
{
    public string ApiKey { get; set; } = default!;
}

