using System.Text;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Qomplement;

public sealed class QomplementClient
{
    private readonly HttpClient _client;

    public QomplementClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<JsonNode?> PostMultipartAsync(string path, MultipartFormDataContent form, CancellationToken cancellationToken)
    {
        using var response = await _client.PostAsync(path, form, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Qomplement POST '{path}' failed ({(int)response.StatusCode}): {text}");

        return ParseJsonNode(text);
    }

    public async Task<JsonNode?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(path, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Qomplement GET '{path}' failed ({(int)response.StatusCode}): {text}");

        return ParseJsonNode(text);
    }

    public async Task<(byte[] Bytes, string? ContentType)> DownloadAsync(string pathOrUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pathOrUrl);
        using var response = await _client.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var text = bytes.Length > 0 ? Encoding.UTF8.GetString(bytes) : string.Empty;
            throw new InvalidOperationException($"Qomplement download '{pathOrUrl}' failed ({(int)response.StatusCode}): {text}");
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

