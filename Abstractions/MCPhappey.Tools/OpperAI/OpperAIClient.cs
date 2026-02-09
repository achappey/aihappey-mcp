using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.OpperAI;

public class OpperAIClient
{
    private readonly HttpClient _client;

    public OpperAIClient(HttpClient client, OpperAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.opper.ai/v2/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}

public class OpperAISettings
{
    public string ApiKey { get; set; } = default!;
}

