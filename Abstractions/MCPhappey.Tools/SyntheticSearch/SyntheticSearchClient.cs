using System.Net.Http.Headers;
using System.Text;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.SyntheticSearch;

public class SyntheticSearchClient
{
    private readonly HttpClient _client;

    public SyntheticSearchClient(HttpClient client, SyntheticSearchSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.synthetic.new/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<HttpResponseMessage> SearchAsync(string body, CancellationToken ct)
    {
        return await _client.PostAsync("v2/search",
            new StringContent(body, Encoding.UTF8, MimeTypes.Json), ct);
    }
}

public class SyntheticSearchSettings
{
    public string ApiKey { get; set; } = default!;
}
