using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.QuiverAI;

public sealed class QuiverAIClient
{
    private readonly HttpClient _client;

    public QuiverAIClient(HttpClient client, QuiverAISettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.quiver.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public HttpRequestMessage CreateJsonPost(string path, JsonNode body)
        => new(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, MimeTypes.Json)
        };

    public async Task<JsonNode?> PostJsonAsync(string path, JsonNode body, CancellationToken cancellationToken)
    {
        using var request = CreateJsonPost(path, body);
        using var response = await _client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
        => _client.SendAsync(request, completionOption, cancellationToken);
}

public sealed class QuiverAISettings
{
    public string ApiKey { get; set; } = default!;
}

