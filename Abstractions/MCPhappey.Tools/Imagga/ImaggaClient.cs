using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Imagga;

public class ImaggaClient
{
    private readonly HttpClient _client;

    private readonly static JsonSerializerOptions jsonSettings = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ImaggaClient(HttpClient client, ImaggaSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.imagga.com/v2/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, jsonSettings);

        return await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
    }

    // === Endpoints ===
    public Task<HttpResponseMessage> AutoTagAsync(object body, string? taggerId, CancellationToken ct)
        => PostAsync(string.IsNullOrWhiteSpace(taggerId) ? "tags" : $"tags/{taggerId}", body, ct);

    public Task<HttpResponseMessage> AnalyzeColorsAsync(object body, CancellationToken ct)
        => PostAsync("colors", body, ct);

    public Task<HttpResponseMessage> CategorizeAsync(object body, string categorizerId, CancellationToken ct)
        => PostAsync($"categories/{categorizerId}", body, ct);

    public Task<HttpResponseMessage> SmartCropAsync(object body, CancellationToken ct)
        => PostAsync("croppings", body, ct);
}


public class ImaggaSettings
{
    public string ApiKey { get; set; } = default!;
}
