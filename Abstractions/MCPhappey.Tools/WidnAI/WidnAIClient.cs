using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.WidnAI;

public sealed class WidnAIClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WidnAIClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<JsonNode?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(path, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI GET '{path}' failed ({(int)response.StatusCode}): {text}");

        return ParseJsonNode(text);
    }

    public async Task PostJsonNoContentAsync(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI POST '{path}' failed ({(int)response.StatusCode}): {text}");
    }

    public async Task<JsonNode?> PostJsonAsync(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var response = await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI POST '{path}' failed ({(int)response.StatusCode}): {text}");

        return ParseJsonNode(text);
    }

    public async Task PutJsonNoContentAsync(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        using var response = await _client.PutAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI PUT '{path}' failed ({(int)response.StatusCode}): {text}");
    }

    public async Task DeleteNoContentAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.DeleteAsync(path, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI DELETE '{path}' failed ({(int)response.StatusCode}): {text}");
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent form, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(path, form, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WidnAI multipart POST '{path}' failed ({(int)response.StatusCode}): {text}");

        return ParseJsonNode(text);
    }

    public async Task<(byte[] Bytes, string? ContentType)> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(path, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var text = bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"WidnAI download '{path}' failed ({(int)response.StatusCode}): {text}");
        }

        return (bytes, response.Content.Headers.ContentType?.MediaType);
    }

    private static JsonNode ParseJsonNode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        return JsonNode.Parse(text) ?? new JsonObject { ["content"] = text };
    }
}

