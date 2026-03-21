using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Flexprice;

internal sealed class FlexpriceClient
{
    private const string JsonMimeType = "application/json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public FlexpriceClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://us.api.flexprice.io/v1/");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMimeType));
    }

    public Task<T?> GetAsync<T>(string relativePath, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, relativePath, null, cancellationToken);

    public Task<T?> PostAsync<T>(string relativePath, object? payload, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Post, relativePath, payload, cancellationToken);

    public Task<T?> PutAsync<T>(string relativePath, object? payload, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Put, relativePath, payload, cancellationToken);

    public Task<T?> DeleteAsync<T>(string relativePath, object? payload, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Delete, relativePath, payload, cancellationToken);

    private async Task<T?> SendAsync<T>(HttpMethod method, string relativePath, object? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath.TrimStart('/'));

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, JsonMimeType);
        }

        using var response = await _client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError(relativePath, response.StatusCode, raw);

        if (string.IsNullOrWhiteSpace(raw))
            return default;

        return JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private static Exception BuildError(string path, HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var message = node?["error"]?["message"]?.GetValue<string>()
                ?? node?["message"]?.GetValue<string>()
                ?? node?["detail"]?.GetValue<string>()
                ?? node?["error"]?.ToJsonString();

            if (!string.IsNullOrWhiteSpace(message))
                return new ValidationException($"Flexprice {path} failed ({(int)statusCode}): {message}");
        }
        catch
        {
            // Ignore parse errors and fall back to the raw response body.
        }

        return new ValidationException($"Flexprice {path} failed ({(int)statusCode}): {raw}");
    }
}
