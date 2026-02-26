using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Daglo;

public sealed class DagloClient
{
    private readonly HttpClient _httpClient;

    public DagloClient(HttpClient httpClient, DagloSettings settings)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri("https://apis.daglo.ai/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        if (!_httpClient.DefaultRequestHeaders.Accept.Any(a =>
                string.Equals(a.MediaType, MimeTypes.Json, StringComparison.OrdinalIgnoreCase)))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        }
    }

    public async Task<JsonDocument> PostJsonAsync(string path, object body, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseText);

        return JsonDocument.Parse(responseText);
    }

    public async Task<byte[]> PostForBytesAsync(string path, object body, string acceptMimeType, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, MimeTypes.Json)
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptMimeType));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var bodyText = Encoding.UTF8.GetString(bytes);
            throw new InvalidOperationException($"Daglo request failed ({(int)response.StatusCode}): {bodyText}");
        }

        return bytes;
    }

    public async Task<JsonDocument> PostMultipartAsync(string path, MultipartFormDataContent body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseText);

        return JsonDocument.Parse(responseText);
    }

    public async Task<JsonDocument?> GetJsonOrNullAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseText);

        return JsonDocument.Parse(responseText);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Daglo request failed ({(int)response.StatusCode}): {body}");
    }
}

