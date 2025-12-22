using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Mistral;

public class MistralClient
{
    private readonly HttpClient _client;

    public MistralClient(HttpClient client, MistralSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.mistral.ai/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        return await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
    }

    // 🧠 Shared helper for DELETE
    private async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct)
        => await _client.DeleteAsync(path, ct);

    // === 📚 Libraries ===
    public Task<HttpResponseMessage> CreateLibraryAsync(object body, CancellationToken ct)
        => PostAsync("libraries", body, ct);

    public Task<HttpResponseMessage> DeleteLibraryAsync(string id, CancellationToken ct)
        => DeleteAsync($"libraries/{id}", ct);

    // === 🤖 Agents ===
    public Task<HttpResponseMessage> CreateAgentAsync(object body, CancellationToken ct)
        => PostAsync("agents", body, ct);

    public Task<HttpResponseMessage> DeleteAgentAsync(string id, CancellationToken ct)
        => DeleteAsync($"agents/{id}", ct);

    // === 💬 Conversations (agent runs) ===
    public Task<HttpResponseMessage> RunAgentConversationAsync(object body, CancellationToken ct)
        => PostAsync("conversations", body, ct);
}

public class MistralSettings
{
    public string ApiKey { get; set; } = default!;
}

public static class MistralConstants
{
    public const string ICON_SOURCE = "https://upload.wikimedia.org/wikipedia/commons/thumb/e/e6/Mistral_AI_logo_%282025%E2%80%93%29.svg/1200px-Mistral_AI_logo_%282025%E2%80%93%29.svg.png";
}



