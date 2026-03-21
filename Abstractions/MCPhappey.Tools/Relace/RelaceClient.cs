using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Relace;

public sealed class RelaceClient
{
    private static readonly JsonSerializerOptions IgnoreNullWebOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public RelaceClient(HttpClient client, RelaceSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri(settings.BaseUrl);

        if (!_client.DefaultRequestHeaders.Accept.Any(a => a.MediaType == "application/json"))
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _client.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    public async Task<JsonNode> SendJsonAsync(HttpMethod method, string relativeUrl, JsonNode payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(payload.ToJsonString(IgnoreNullWebOptions), Encoding.UTF8, "application/json")
        };

        using var response = await _client.SendAsync(request, cancellationToken);
        return await ParseJsonResponseAsync(response, cancellationToken);
    }

    public async Task<JsonNode> SendBinaryAsync(HttpMethod method, string relativeUrl, byte[] contentBytes, string? mimeType, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        var content = new ByteArrayContent(contentBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType)
            ? "application/octet-stream"
            : mimeType);
        request.Content = content;

        using var response = await _client.SendAsync(request, cancellationToken);
        return await ParseJsonResponseAsync(response, cancellationToken);
    }

    public async Task SendNoContentAsync(HttpMethod method, string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        using var response = await _client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Relace API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static async Task<JsonNode> ParseJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Relace API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

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

public sealed class RelaceSettings
{
    public string ApiKey { get; set; } = default!;

    public string BaseUrl { get; set; } = "https://api.relace.run/";
}
