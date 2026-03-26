using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.CaseDev;

public sealed class CaseDevClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly CaseDevSettings _settings;

    public CaseDevClient(HttpClient httpClient, CaseDevSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpClient.BaseAddress ??= new Uri(settings.BaseUrl);
    }

    public Task<CaseDevResponse> GetAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    public Task<CaseDevResponse> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), cancellationToken);

    public Task<CaseDevResponse> SendJsonAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body?.ToJsonString(JsonOptions) ?? "{}", Encoding.UTF8, "application/json")
        };

        return SendAsync(request, cancellationToken);
    }

    public Task<CaseDevResponse> SendMultipartAsync(string path, MultipartFormDataContent form, CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = form }, cancellationToken);

    private async Task<CaseDevResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ApplyHeaders(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        JsonNode? json = null;

        if (IsJsonContentType(contentType) && bytes.Length > 0)
        {
            json = JsonNode.Parse(bytes);
        }

        var result = new CaseDevResponse(response.StatusCode, contentType, bytes, json);

        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.Text)
                ? $"Case.dev request failed with status {(int)result.StatusCode} ({result.StatusCode})."
                : $"Case.dev request failed with status {(int)result.StatusCode} ({result.StatusCode}): {result.Text}";
            throw new InvalidOperationException(error);
        }

        return result;
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        foreach (var header in _settings.Headers)
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;

            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static bool IsJsonContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType)
           && (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}

public sealed record CaseDevResponse(HttpStatusCode StatusCode, string? ContentType, byte[] Bytes, JsonNode? Json)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    public string Text => Bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Bytes);
}

public sealed class CaseDevSettings
{
    public string BaseUrl { get; set; } = "https://api.case.dev";

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
