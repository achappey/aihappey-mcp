using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.ZAI;

public class ZAIClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ZAIClient(HttpClient client, ZAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.z.ai/api/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> PostAsync(
        string path,
        object body,
        IDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        if (headers != null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

public class ZAISettings
{
    public string ApiKey { get; set; } = default!;
}
