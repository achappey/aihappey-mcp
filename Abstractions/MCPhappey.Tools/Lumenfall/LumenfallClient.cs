using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Lumenfall;

public sealed class LumenfallClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LumenfallClient(HttpClient client, LumenfallSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.lumenfall.ai/openai/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> PostJsonAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = form
        };

        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(path.TrimStart('/'), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.DeleteAsync(path.TrimStart('/'), cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new Exception($"{response.StatusCode}: {text}");
    }
}

public sealed class LumenfallSettings
{
    public string ApiKey { get; set; } = default!;
}

