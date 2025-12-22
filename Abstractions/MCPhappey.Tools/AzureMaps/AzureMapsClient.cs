using System.Net.Http.Headers;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.AzureMaps;

public class AzureMapsClient
{
    private readonly HttpClient _client;
    private readonly AzureMapsSettings _settings;
    private const string BaseUrl = "https://atlas.microsoft.com";

    public AzureMapsClient(HttpClient client, AzureMapsSettings settings)
    {
        _client = client;
        _settings = settings;

        _client.BaseAddress ??= new Uri(BaseUrl.TrimEnd('/') + "/");
        _client.DefaultRequestHeaders.Add("Subscription-Key", _settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        req.Headers.Add("Subscription-Key", _settings.ApiKey);
        return await _client.SendAsync(req, ct);
    }

    public async Task<byte[]> GetBytesAsync(string relativeUrl, CancellationToken ct)
    {
        using var resp = await GetAsync(relativeUrl, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new Exception($"Azure Maps error {(int)resp.StatusCode} {resp.ReasonPhrase}: {err}");
        }
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<string> GetStringAsync(string relativeUrl, CancellationToken ct)
    {
        using var resp = await GetAsync(relativeUrl, ct);
        var txt = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Azure Maps error {(int)resp.StatusCode} {resp.ReasonPhrase}: {txt}");
        return txt;
    }
}

public class AzureMapsSettings
{
    public string ApiKey { get; set; } = default!;
}

