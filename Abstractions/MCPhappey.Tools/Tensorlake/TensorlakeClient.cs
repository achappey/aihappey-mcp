using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Tensorlake;

public sealed class TensorlakeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;

    public TensorlakeClient(HttpClient httpClient, TensorlakeSettings settings)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri("https://api.tensorlake.ai/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
    }

    public async Task<JsonObject> UploadFileAsync(byte[] contents, string fileName, string mimeType, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(contents);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        form.Add(fileContent, "file_bytes", string.IsNullOrWhiteSpace(fileName) ? "input.bin" : fileName);

        using var request = new HttpRequestMessage(HttpMethod.Put, "documents/v2/files")
        {
            Content = form
        };

        return await SendForJsonObjectAsync(request, cancellationToken);
    }

    public async Task<JsonObject> CreateReadJobAsync(JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "documents/v2/read")
        {
            Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
        };

        return await SendForJsonObjectAsync(request, cancellationToken);
    }

    public async Task<JsonObject> GetParseAsync(string parseId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"documents/v2/parse/{Uri.EscapeDataString(parseId)}");
        return await SendForJsonObjectAsync(request, cancellationToken);
    }

    public async Task DeleteParseAsync(string parseId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"documents/v2/parse/{Uri.EscapeDataString(parseId)}");
        await SendForNoContentAsync(request, cancellationToken);
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"documents/v2/files/{Uri.EscapeDataString(fileId)}");
        await SendForNoContentAsync(request, cancellationToken);
    }

    private async Task<JsonObject> SendForJsonObjectAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Tensorlake request failed ({(int)response.StatusCode} {response.StatusCode}): {text}");

        var json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        return json ?? throw new Exception("Tensorlake returned an empty or invalid JSON payload.");
    }

    private async Task SendForNoContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new Exception($"Tensorlake cleanup failed ({(int)response.StatusCode} {response.StatusCode}): {text}");
    }
}
