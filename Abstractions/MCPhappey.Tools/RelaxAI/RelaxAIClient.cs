using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.RelaxAI;

public sealed class RelaxAIClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public RelaxAIClient(HttpClient client, RelaxAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri(settings.BaseUrl);

        _client.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        if (!_client.DefaultRequestHeaders.Accept.Any(a => a.MediaType == MimeTypes.Json))
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonElement> PostJsonAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await _client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"RelaxAI API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");

        if (string.IsNullOrWhiteSpace(body))
            return JsonSerializer.SerializeToElement(new { }, JsonOptions);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch
        {
            return JsonSerializer.SerializeToElement(new { raw = body }, JsonOptions);
        }
    }
}

public sealed class RelaxAISettings
{
    public string ApiKey { get; set; } = default!;

    public string BaseUrl { get; set; } = "https://api.relax.ai/v1/";
}
