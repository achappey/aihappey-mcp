using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.APIpie;

public class APIpieClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public APIpieClient(HttpClient client, APIpieSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://apipie.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonElement?> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        return await SendAndParseAsync(req, ct);
    }

    public async Task<JsonElement?> PostMultipartAsync(string path, MultipartFormDataContent form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = form
        };

        return await SendAndParseAsync(req, ct);
    }

    private async Task<JsonElement?> SendAndParseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        if (string.IsNullOrWhiteSpace(text))
            return null;

        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone(); // IMPORTANT
    }
}

public class APIpieSettings
{
    public string ApiKey { get; set; } = default!;
}