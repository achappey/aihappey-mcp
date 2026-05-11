using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Loreto;

public sealed class LoretoClient
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public LoretoClient(HttpClient client, LoretoSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.loreto.io/");
        _client.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GenerateAsync(
        string source,
        string sourceType,
        string testLanguage,
        bool includeVisuals,
        string? context,
        IReadOnlyList<string>? themesToProcess,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            source,
            sourceType,
            testLanguage,
            includeVisuals,
            context,
            themesToProcess = themesToProcess is { Count: > 0 } ? themesToProcess : null
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/skills/generate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return await SendJsonAsync(message, cancellationToken);
    }

    public async Task<JsonElement> UploadAndGenerateAsync(
        string fileName,
        BinaryData content,
        string? mimeType,
        string testLanguage,
        bool includeVisuals,
        string? context,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);

        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "source" : fileName);
        form.Add(new StringContent(testLanguage), "test_language");
        form.Add(new StringContent(includeVisuals ? "true" : "false"), "include_visuals");

        if (!string.IsNullOrWhiteSpace(context))
            form.Add(new StringContent(context), "context");

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/skills/generate/upload")
        {
            Content = form
        };

        return await SendJsonAsync(message, cancellationToken);
    }

    public async Task<JsonElement> HealthAsync(CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "api/v1/health");
        return await SendJsonAsync(message, cancellationToken);
    }

    private async Task<JsonElement> SendJsonAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        using var response = await _client.SendAsync(message, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject().ToJsonElement();

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}

public sealed class LoretoSettings
{
    public string ApiKey { get; set; } = default!;
}
