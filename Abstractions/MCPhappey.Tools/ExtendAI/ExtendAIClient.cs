using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.ExtendAI;

public class ExtendAIClient
{
    private const string ApiVersion = "2026-02-09";
    private readonly HttpClient _client;

    public ExtendAIClient(HttpClient client, ExtendAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.extend.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        if (!_client.DefaultRequestHeaders.Contains("x-extend-api-version"))
            _client.DefaultRequestHeaders.Add("x-extend-api-version", ApiVersion);
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

        return ParseResponseAsJsonNode(text);
    }

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return ParseResponseAsJsonNode(text);
    }

    public async Task<HttpResponseMessage> RawSendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new Exception($"{response.StatusCode}: {text}");
        }

        return response;
    }

    private static JsonNode ParseResponseAsJsonNode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        return JsonNode.Parse(text) ?? new JsonObject
        {
            ["content"] = text
        };
    }
}

public class ExtendAISettings
{
    public string ApiKey { get; set; } = default!;
}

public static class ExtendAIConstants
{
    public const string ICON_SOURCE = "https://extend.ai/favicon.ico";
}
