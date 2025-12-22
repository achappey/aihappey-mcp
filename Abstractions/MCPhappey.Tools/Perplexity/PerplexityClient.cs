using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Perplexity.Clients;

public class PerplexityClient
{
    private readonly HttpClient _client;

    public PerplexityClient(HttpClient client, PerplexitySettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.perplexity.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<HttpResponseMessage> SearchAsync(object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        return await _client.PostAsync("search",
            new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
    }
}


public class PerplexitySettings
{
    public string ApiKey { get; set; } = default!;
}
