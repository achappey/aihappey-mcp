using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.LumaAI;

public sealed class LumaAIClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LumaAIClient(HttpClient client, LumaAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.lumalabs.ai/dream-machine/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> PostAsync(string path, object body, CancellationToken cancellationToken)
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

public sealed class LumaAISettings
{
    public string ApiKey { get; set; } = default!;
}

