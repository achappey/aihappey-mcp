using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Parallel.Clients;

public class ParallelClient
{
    private readonly HttpClient _client;

    private readonly static JsonSerializerOptions jsonSettings = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ParallelClient(HttpClient client, ParallelSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://api.parallel.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    // 🧠 Shared helper for POST with optional beta header
    public async Task<HttpResponseMessage> PostAsync(string path, object body, string? betaHeader, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(betaHeader))
        {
            if (_client.DefaultRequestHeaders.Contains("parallel-beta"))
                _client.DefaultRequestHeaders.Remove("parallel-beta");
            _client.DefaultRequestHeaders.Add("parallel-beta", betaHeader);
        }

        var json = JsonSerializer.Serialize(body, jsonSettings);

        return await _client.PostAsync(path, new StringContent(json, Encoding.UTF8, MimeTypes.Json), ct);
    }

    // === Extract ===
    public Task<HttpResponseMessage> ExtractAsync(object body, CancellationToken ct)
        => PostAsync("v1beta/extract", body, "search-extract-2025-10-10", ct);

    // === Search ===
    public Task<HttpResponseMessage> SearchAsync(object body, CancellationToken ct)
        => PostAsync("v1beta/search", body, "search-query-2025-10-10", ct);

    // === Tasks ===
    public Task<HttpResponseMessage> CreateTaskAsync(object body, CancellationToken ct)
        => PostAsync("v1beta/tasks/runs", body, "mcp-server-2025-07-17,events-sse-2025-07-24,webhook-2025-08-12", ct);

    public Task<HttpResponseMessage> CreateTaskGroupAsync(object body, CancellationToken ct)
        => PostAsync("v1beta/tasks/groups", body, "taskgroup-2025-10-10", ct);
}
