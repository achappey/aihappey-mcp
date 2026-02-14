using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.SiliconFlow;

public class SiliconFlowClient
{
    private readonly HttpClient _client;

    public SiliconFlowClient(HttpClient client, SiliconFlowSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.siliconflow.com/v1/");
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
}

public class SiliconFlowSettings
{
    public string ApiKey { get; set; } = default!;
}

