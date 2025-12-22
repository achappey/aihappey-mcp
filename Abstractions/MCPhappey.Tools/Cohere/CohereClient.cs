using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Cohere;

public class CohereClient
{
    private readonly HttpClient _client;

    public CohereClient(HttpClient client, CohereSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.cohere.com/v2/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
        var clientName = assemblyName?.Split('.').Last() ?? "Unknown";
        client.DefaultRequestHeaders.Add("X-Client-Name", clientName);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> RerankAsync(string model, string query, List<string> documents, int topN, CancellationToken ct)
    {
        var body = new
        {
            model,
            query,
            documents,
            top_n = topN
        };

        var json = JsonSerializer.Serialize(body, JsonOpts);

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

public class CohereSettings
{
    public string ApiKey { get; set; } = default!;
}
