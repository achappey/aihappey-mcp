using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.AIML;

public class AIMLClient
{
    private readonly HttpClient _client;

    public AIMLClient(HttpClient client, AIMLSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.aimlapi.com/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonDocument> PostAsync(string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MimeTypes.Json);

        using var resp = await _client.SendAsync(request, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonDocument.Parse(text);
    }

}

public class AIMLSettings
{
    public string ApiKey { get; set; } = default!;
}