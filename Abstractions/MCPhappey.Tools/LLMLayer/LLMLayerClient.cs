using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.LLMLayer;

public sealed class LLMLayerClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LLMLayerClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.llmlayer.dev/api/v2/");
    }

    public async Task<JsonNode?> PostJsonAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var response = await _client.PostAsync(relativePath, new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError(relativePath, response.StatusCode, raw);

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    public async Task<HttpResponseMessage> PostSseAsync(string relativePath, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static Exception BuildError(string path, System.Net.HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var detail = node?["detail"] as JsonObject;
            var errorCode = detail?["error_code"]?.GetValue<string>();
            var message = detail?["message"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(message))
                return new Exception($"LLMLayer {path} failed ({(int)statusCode}) {errorCode}: {message}");
        }
        catch
        {
            // ignore parse errors and fallback to raw
        }

        return new Exception($"LLMLayer {path} failed ({(int)statusCode}): {raw}");
    }
}

