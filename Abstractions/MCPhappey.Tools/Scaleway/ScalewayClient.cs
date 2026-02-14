using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Scaleway;

public class ScalewayClient
{
    private readonly HttpClient _client;

    public ScalewayClient(HttpClient client, ScalewaySettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.scaleway.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> PostRerankAsync(string? projectId, object body, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(projectId)
            ? "v1/rerank"
            : $"{projectId}/v1/rerank";

        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}

public class ScalewaySettings
{
    public string ApiKey { get; set; } = default!;
}

