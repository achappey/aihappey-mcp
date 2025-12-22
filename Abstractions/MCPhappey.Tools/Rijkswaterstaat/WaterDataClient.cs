using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Rijkswaterstaat;

public class WaterDataClient
{
    private readonly HttpClient _client;
    
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WaterDataClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://waterwebservices.beta.rijkswaterstaat.nl/test/");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        // dynamic X-Client-Name = short assembly name
        var asm = Assembly.GetExecutingAssembly().GetName().Name;
        var name = asm?.Split('.').Last() ?? "Unknown";
        _client.DefaultRequestHeaders.Add("X-Client-Name", name);
    }

    public async Task<JsonNode?> PostAsync(string relativePath, JsonNode body, CancellationToken ct)
    {
        var json = body?.ToJsonString(JsonOpts) ?? "{}";
        using var req = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        // optional future debug key
        req.Headers.Add("X-API-KEY", "dummy");

        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);

        // handle 204
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            return new JsonObject { ["status"] = "no_content", ["message"] = "No data found." };

        // 2xx
        if (resp.IsSuccessStatusCode)
            return TryParse(payload, "ok", (int)resp.StatusCode);

        // 404 may still contain useful body
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return TryParse(payload, "not_found", (int)resp.StatusCode);

        // other errors
        return TryParse(payload, "error", (int)resp.StatusCode);
    }

    private static JsonNode TryParse(string json, string status, int httpStatus)
    {
        try
        {
            var parsed = JsonNode.Parse(json);
            if (parsed is not null) return parsed;
        }
        catch { /* fall through */ }

        return new JsonObject
        {
            ["status"] = status,
            ["httpStatus"] = httpStatus,
            ["raw"] = json
        };
    }
}
