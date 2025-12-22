using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.AsyncAI;

public class AsyncAIClient
{
    private readonly HttpClient _client;
    private readonly AsyncAISettings _settings;

    public AsyncAIClient(HttpClient client, AsyncAISettings settings)
    {
        _client = client;
        _settings = settings;

        _client.BaseAddress ??= new Uri("https://api.async.ai/");
        _client.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);
        _client.DefaultRequestHeaders.Add("version", "v1");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<byte[]> TextToSpeechAsync(object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "text_to_speech")
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new Exception($"AsyncAI TTS failed: {err}");
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<T> PostJsonAsync<T>(string endpoint, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new Exception($"AsyncAI call failed: {err}");
        }

        var result = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        if (result == null)
            throw new InvalidOperationException("Invalid JSON response from AsyncAI.");
        return result;
    }

    public async Task<byte[]> TextToSpeechWithTimestampsAsync(object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "text_to_speech/with_timestamps")
        {
            Content = JsonContent.Create(body)
        };

        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new Exception($"AsyncAI with timestamps failed: {err}");
        }

        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}


public class AsyncAISettings
{
    public string ApiKey { get; set; } = default!;
}
