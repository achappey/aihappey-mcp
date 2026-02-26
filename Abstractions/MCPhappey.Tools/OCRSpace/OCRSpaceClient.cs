using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.OCRSpace;

public sealed class OCRSpaceClient
{
    private readonly HttpClient _client;

    public OCRSpaceClient(HttpClient client, OCRSpaceSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.ocr.space/");

        if (_client.DefaultRequestHeaders.Contains("apikey"))
            _client.DefaultRequestHeaders.Remove("apikey");

        _client.DefaultRequestHeaders.Add("apikey", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"OCR.space request failed ({response.StatusCode}): {raw}");

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }
}

public sealed class OCRSpaceSettings
{
    public string ApiKey { get; set; } = default!;
}

