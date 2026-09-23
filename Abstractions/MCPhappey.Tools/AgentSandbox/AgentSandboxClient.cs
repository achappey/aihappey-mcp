using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.AgentSandbox;

public sealed class AgentSandboxClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<AgentSandboxResponse> PostJsonAsync(
        string path,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        return SendAsync(new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = content
        }, cancellationToken);
    }

    public Task<AgentSandboxResponse> PostMultipartAsync(
        string path,
        MultipartFormDataContent content,
        CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = content
        }, cancellationToken);

    public Task<AgentSandboxResponse> DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, path.TrimStart('/')), cancellationToken);

    private async Task<AgentSandboxResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await httpClient.SendAsync(request, cancellationToken))
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            JsonNode? json = null;

            if (bytes.Length > 0 && IsJson(mediaType))
                json = JsonNode.Parse(bytes);

            var result = new AgentSandboxResponse(response.StatusCode, bytes, json);
            if (!result.IsSuccess)
            {
                var details = string.IsNullOrWhiteSpace(result.Text) ? response.ReasonPhrase : result.Text;
                throw new InvalidOperationException(
                    $"AgentSandbox API request failed with status {(int)response.StatusCode} ({response.StatusCode}): {details}");
            }

            return result;
        }
    }

    private static bool IsJson(string? mediaType)
        => !string.IsNullOrWhiteSpace(mediaType)
           && (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
               || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}

public sealed record AgentSandboxResponse(HttpStatusCode StatusCode, byte[] Bytes, JsonNode? Json)
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

public sealed class AgentSandboxSettings
{
    public string ApiKey { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.agentsandbox.co/";
}
