using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.JsonReceipt;

public sealed class JsonReceiptClient
{
    private readonly HttpClient _client;

    public JsonReceiptClient(HttpClient client, JsonReceiptSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://jsonreceipts.com/");

        if (_client.DefaultRequestHeaders.Contains("X-API-Key"))
            _client.DefaultRequestHeaders.Remove("X-API-Key");

        _client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"JsonReceipt request failed ({response.StatusCode}): {raw}");

        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }
}

public sealed class JsonReceiptSettings
{
    public string ApiKey { get; set; } = default!;
}

