using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Recraft;

public class RecraftClient
{
    private readonly HttpClient _client;

    public RecraftClient(HttpClient client, RecraftSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri("https://external.api.recraft.ai/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonNode?> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, MimeTypes.Json)
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    public async Task<JsonNode?> PostMultipartAsync(
        string path,
        Dictionary<string, string?> fields,
        Dictionary<string, FileItem> files,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();

        foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
            form.Add(new StringContent(field.Value!), field.Key);

        foreach (var file in files)
        {
            var data = file.Value;
            var bytes = data.Contents.ToArray();
            var content = new ByteArrayContent(bytes);

            if (!string.IsNullOrWhiteSpace(data.MimeType) &&
                MediaTypeHeaderValue.TryParse(data.MimeType, out var mediaType))
            {
                content.Headers.ContentType = mediaType;
            }

            form.Add(content, file.Key, data.Filename ?? $"{file.Key}.bin");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = form
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

public class RecraftSettings
{
    public string ApiKey { get; set; } = default!;
}

