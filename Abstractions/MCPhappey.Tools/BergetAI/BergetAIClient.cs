using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.BergetAI;

public class BergetAIClient
{
    private readonly HttpClient _client;

    public BergetAIClient(HttpClient client, BergetAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.berget.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonElement?> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);

        using var response = await _client.PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, MimeTypes.Json),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"{response.StatusCode}: {errorText}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        if (stream.CanSeek && stream.Length == 0)
            return null;

        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}

public class BergetAISettings
{
    public string ApiKey { get; set; } = default!;
}

