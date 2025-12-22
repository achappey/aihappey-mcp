using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.SPAMhunter;

public class SPAMhunterClient
{
    private readonly HttpClient _client;

    public SPAMhunterClient(HttpClient client, SPAMhunterSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.spamhunter.io/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("X-API-Key", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> CheckAsync(string ip, string content, CancellationToken ct)
    {
        var body = new
        {
            ip,
            content
        };

        return await PostAsync("check", body, ct);
    }

    private async Task<JsonNode?> PostAsync(string endpoint, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }
}


public class SPAMhunterSettings
{
    public string ApiKey { get; set; } = default!;
}

