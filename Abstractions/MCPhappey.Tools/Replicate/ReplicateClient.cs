using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Replicate;

public class ReplicateClient
{
    private readonly HttpClient _client;

    public ReplicateClient(HttpClient client, ReplicateSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.replicate.com/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> CreatePredictionAsync(
        string version,
        JsonElement input,
        string? cancelAfter,
        int? preferWaitSeconds,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/predictions")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                version,
                input
            }, JsonOpts), Encoding.UTF8, MimeTypes.Json)
        };

        if (!string.IsNullOrWhiteSpace(cancelAfter))
            req.Headers.Add("Cancel-After", cancelAfter);

        if (preferWaitSeconds is > 0)
            req.Headers.Add("Prefer", $"wait={preferWaitSeconds}");

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}

public class ReplicateSettings
{
    public string ApiKey { get; set; } = default!;
}
