using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Pinecone;

public sealed class PineconeClient
{
    private const string ApiVersion = "2025-10";
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PineconeClient(HttpClient client, PineconeSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.pinecone.io/");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Api-Key", settings.ApiKey);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Pinecone-Api-Version", ApiVersion);
    }

    public async Task<JsonNode?> RerankAsync(JsonObject payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "rerank")
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}

public sealed class PineconeSettings
{
    public string ApiKey { get; set; } = default!;
}
