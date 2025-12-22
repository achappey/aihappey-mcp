using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.JinaAI;

public class JinaAIClient
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JinaAIClient(HttpClient client, JinaAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.jina.ai/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> RerankAsync(string model, string query, List<string> documents, int topN, CancellationToken ct)
    {
        var body = new
        {
            model,
            query,
            top_n = topN,
            documents,
            return_documents = true
        };

        return await PostAsync("rerank", body, ct);
    }

    public async Task<JsonNode?> ClassifyAsync(string model, List<string> inputs, List<string> labels, CancellationToken ct)
    {
        var body = new
        {
            model,
            input = inputs,
            labels
        };

        return await PostAsync("classify", body, ct);
    }

    private async Task<JsonNode?> PostAsync(string endpoint, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
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


public class JinaAISettings
{
    public string ApiKey { get; set; } = default!;
}

