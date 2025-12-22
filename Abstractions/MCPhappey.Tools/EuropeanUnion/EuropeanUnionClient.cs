using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.EuropeanUnion;

public class EuropeanUnionClient
{
    private readonly HttpClient _client;

    public EuropeanUnionClient(HttpClient client)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://ec.europa.eu/taxation_customs/vies/rest-api/");
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        using var resp = await _client.GetAsync(path, ct);
        return await ParseResponse(resp);
    }

    public async Task<JsonNode?> PostAsync(string path, JsonNode body, CancellationToken ct)
    {
        var json = body.ToJsonString();
        using var resp = await _client.PostAsync(path,
            new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
        
        return await ParseResponse(resp);
    }

    private static async Task<JsonNode?> ParseResponse(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        try
        {
            return JsonNode.Parse(text);
        }
        catch
        {
            return new JsonObject
            {
                ["status"] = (int)resp.StatusCode,
                ["raw"] = text
            };
        }
    }
}
