using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.WebCrawlerAPI;

public sealed class WebCrawlerAPIClient
{
    public const string BaseUrl = "https://api.webcrawlerapi.com";

    private static readonly JsonSerializerOptions IgnoreNullWebOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public WebCrawlerAPIClient(HttpClient client, WebCrawlerAPISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri($"{BaseUrl}/");

        if (!_client.DefaultRequestHeaders.Accept.Any(a => a.MediaType == "application/json"))
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _client.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    public async Task<JsonNode> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await _client.SendAsync(request, cancellationToken);
        return await ParseResponseAsync(response, cancellationToken);
    }

    public async Task<WebCrawlerAPITextResponse> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _client.SendAsync(request, cancellationToken);
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WebCrawlerAPI text download error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        return new WebCrawlerAPITextResponse(
            body,
            response.Content?.Headers.ContentType?.MediaType,
            (int)response.StatusCode);
    }

    public async Task<JsonNode> SendJsonAsync(HttpMethod method, string relativeUrl, JsonNode? payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);

        if (payload is not null)
            request.Content = new StringContent(payload.ToJsonString(IgnoreNullWebOptions), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, cancellationToken);
        return await ParseResponseAsync(response, cancellationToken);
    }

    private static async Task<JsonNode> ParseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"WebCrawlerAPI error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        if (string.IsNullOrWhiteSpace(body))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(body) ?? new JsonObject();
        }
        catch
        {
            return new JsonObject { ["raw"] = body };
        }
    }
}

public sealed class WebCrawlerAPISettings
{
    public string ApiKey { get; set; } = default!;
}

public sealed record WebCrawlerAPITextResponse(string Text, string? ContentType, int StatusCode);
