using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Kirha;

public sealed class KirhaClient
{
    private static readonly Uri ChatBaseUri = new("https://api.kirha.ai/chat/");
    private static readonly Uri BillingBaseUri = new("https://api.kirha.com/billing/v1/");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;

    public KirhaClient(HttpClient client, KirhaSettings settings)
    {
        _client = client;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        if (!_client.DefaultRequestHeaders.Accept.Any(a =>
                string.Equals(a.MediaType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase)))
        {
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        }
    }

    public Task<JsonNode?> GetChatAsync(string pathAndQuery, CancellationToken ct)
        => SendAndParseAsync(new HttpRequestMessage(HttpMethod.Get, BuildUri(ChatBaseUri, pathAndQuery)), ct);

    public Task<JsonNode?> GetBillingAsync(string pathAndQuery, CancellationToken ct)
        => SendAndParseAsync(new HttpRequestMessage(HttpMethod.Get, BuildUri(BillingBaseUri, pathAndQuery)), ct);

    public Task<JsonNode?> PostChatAsync(string path, object body, CancellationToken ct)
        => SendAndParseAsync(CreateJsonRequest(HttpMethod.Post, BuildUri(ChatBaseUri, path), body), ct);

    public Task<JsonNode?> PostBillingAsync(string path, object body, CancellationToken ct)
        => SendAndParseAsync(CreateJsonRequest(HttpMethod.Post, BuildUri(BillingBaseUri, path), body), ct);

    public async Task PostBillingNoContentAsync(string path, object body, CancellationToken ct)
    {
        using var req = CreateJsonRequest(HttpMethod.Post, BuildUri(BillingBaseUri, path), body);
        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, object body)
        => new(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, MimeTypes.Json)
        };

    private async Task<JsonNode?> SendAndParseAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static Uri BuildUri(Uri baseUri, string relativeOrAbsolute)
        => Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute)
            ? absolute
            : new(baseUri, relativeOrAbsolute.TrimStart('/'));
}
