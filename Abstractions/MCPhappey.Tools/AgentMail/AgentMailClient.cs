using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.AgentMail;

public sealed class AgentMailClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly AgentMailSettings _settings;

    public AgentMailClient(HttpClient httpClient, AgentMailSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpClient.BaseAddress ??= new Uri(settings.BaseUrl.TrimEnd('/') + "/");
    }

    public Task<AgentMailResponse> SendJsonAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, path.TrimStart('/'));

        if (method != HttpMethod.Delete)
        {
            request.Content = new StringContent(body?.ToJsonString(JsonOptions) ?? "{}", Encoding.UTF8, "application/json");
        }

        return SendAsync(request, cancellationToken);
    }

    public Task<AgentMailResponse> PostAsync(string path, JsonNode? body, CancellationToken cancellationToken = default)
        => SendJsonAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task<AgentMailResponse> PatchAsync(string path, JsonNode? body, CancellationToken cancellationToken = default)
        => SendJsonAsync(HttpMethod.Patch, path, body, cancellationToken);

    public Task<AgentMailResponse> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => SendJsonAsync(HttpMethod.Delete, path, null, cancellationToken);

    private async Task<AgentMailResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

        var result = new AgentMailResponse(response.StatusCode, contentType, bytes, json);
        if (!result.IsSuccess)
        {
            var error = string.IsNullOrWhiteSpace(result.Text)
                ? $"AgentMail request failed with status {(int)result.StatusCode} ({result.StatusCode})."
                : $"AgentMail request failed with status {(int)result.StatusCode} ({result.StatusCode}): {result.Text}";
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

public sealed record AgentMailResponse(HttpStatusCode StatusCode, string? ContentType, byte[] Bytes, JsonNode? Json)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    public string Text => Bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Bytes);

    public JsonNode StructuredOrStatus(string operation)
        => Json ?? new JsonObject
        {
            ["operation"] = operation,
            ["status"] = (int)StatusCode,
            ["status_text"] = StatusCode.ToString()
        };
}

public sealed class AgentMailSettings
{
    public string BaseUrl { get; set; } = "https://api.agentmail.to";

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
