using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Scrappey;

public sealed class ScrappeyClient(HttpClient client, ScrappeyApiSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<JsonNode?> GetBalanceAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(BuildRelativeUrl("api/v1/balance"), cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError("GET /api/v1/balance", response.StatusCode, raw);

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }

    public async Task<ScrappeyResponse> PostCommandAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var response = await client.PostAsync(
            BuildRelativeUrl("api/v1"),
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw BuildError("POST /api/v1", response.StatusCode, raw);

        return new ScrappeyResponse((int)response.StatusCode, string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw));
    }

    private string BuildRelativeUrl(string path)
        => $"{path.TrimStart('/')}?key={Uri.EscapeDataString(settings.ApiKey)}";

    private static Exception BuildError(string operation, HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var detail = node?["error"]?.GetValue<string>()
                ?? node?["message"]?.GetValue<string>()
                ?? node?["detail"]?.GetValue<string>()
                ?? node?["info"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(detail))
                return new Exception($"Scrappey {operation} failed ({(int)statusCode}): {detail}");
        }
        catch
        {
            // ignored
        }

        return new Exception($"Scrappey {operation} failed ({(int)statusCode}): {raw}");
    }
}

public sealed class ScrappeyApiSettings
{
    public string ApiKey { get; set; } = default!;
}

public sealed record ScrappeyResponse(int StatusCode, JsonNode? Body);

