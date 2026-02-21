using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.DeepL;

public class DeepLClient
{
    private readonly HttpClient _client;

    public DeepLClient(HttpClient client, DeepLSettings settings)
    {
        _client = client;
        _client.BaseAddress ??= new Uri(settings.BaseUrl);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", settings.ApiKey);
    }

    public async Task<JsonNode?> UploadDocumentAsync(
        BinaryData file,
        string filename,
        string targetLang,
        string? sourceLang,
        string? outputFormat,
        string? formality,
        string? glossaryId,
        CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(file.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", filename);

        form.Add(new StringContent(targetLang), "target_lang");

        if (!string.IsNullOrWhiteSpace(sourceLang))
            form.Add(new StringContent(sourceLang), "source_lang");

        if (!string.IsNullOrWhiteSpace(outputFormat))
            form.Add(new StringContent(outputFormat), "output_format");

        if (!string.IsNullOrWhiteSpace(formality))
            form.Add(new StringContent(formality), "formality");

        if (!string.IsNullOrWhiteSpace(glossaryId))
            form.Add(new StringContent(glossaryId), "glossary_id");

        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/document")
        {
            Content = form
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public async Task<JsonNode?> GetDocumentStatusAsync(string documentId, string documentKey, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["document_key"] = documentKey
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"v2/document/{Uri.EscapeDataString(documentId)}")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return JsonNode.Parse(text);
    }

    public async Task<BinaryData> DownloadTranslatedDocumentAsync(string documentId, string documentKey, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["document_key"] = documentKey
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"v2/document/{Uri.EscapeDataString(documentId)}/result")
        {
            Content = new FormUrlEncodedContent(form)
        };

        using var resp = await _client.SendAsync(req, ct);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var text = bytes.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(bytes);
            throw new Exception($"{resp.StatusCode}: {text}");
        }

        return BinaryData.FromBytes(bytes);
    }

    public async Task<JsonNode?> CreateMultilingualGlossaryAsync(
        string name,
        JsonArray dictionaries,
        CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["name"] = name,
            ["dictionaries"] = dictionaries
        };

        return await SendJsonAsync(HttpMethod.Post, "v3/glossaries", payload, ct);
    }

    public async Task<JsonNode?> PatchMultilingualGlossaryAsync(
        string glossaryId,
        string? name,
        JsonArray? dictionaries,
        CancellationToken ct)
    {
        var payload = new JsonObject();

        if (!string.IsNullOrWhiteSpace(name))
            payload["name"] = name;

        if (dictionaries != null)
            payload["dictionaries"] = dictionaries;

        if (payload.Count == 0)
            throw new ArgumentException("At least one of name or dictionaries must be provided.");

        return await SendJsonAsync(
            HttpMethod.Patch,
            $"v3/glossaries/{Uri.EscapeDataString(glossaryId)}",
            payload,
            ct);
    }

    public async Task<JsonNode?> ReplaceDictionaryAsync(
        string glossaryId,
        string sourceLang,
        string targetLang,
        string entries,
        string entriesFormat,
        CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["source_lang"] = sourceLang,
            ["target_lang"] = targetLang,
            ["entries"] = entries,
            ["entries_format"] = entriesFormat
        };

        return await SendJsonAsync(
            HttpMethod.Put,
            $"v3/glossaries/{Uri.EscapeDataString(glossaryId)}/dictionaries",
            payload,
            ct);
    }

    public async Task DeleteGlossaryAsync(string glossaryId, CancellationToken ct)
        => await SendNoContentAsync(HttpMethod.Delete, $"v3/glossaries/{Uri.EscapeDataString(glossaryId)}", ct);

    public async Task DeleteDictionaryAsync(string glossaryId, string sourceLang, string targetLang, CancellationToken ct)
        => await SendNoContentAsync(
            HttpMethod.Delete,
            $"v3/glossaries/{Uri.EscapeDataString(glossaryId)}/dictionaries?source_lang={Uri.EscapeDataString(sourceLang)}&target_lang={Uri.EscapeDataString(targetLang)}",
            ct);

    private async Task<JsonNode?> SendJsonAsync(HttpMethod method, string relativeUrl, JsonObject payload, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, relativeUrl)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private async Task SendNoContentAsync(HttpMethod method, string relativeUrl, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, relativeUrl);
        using var resp = await _client.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{resp.StatusCode}: {text}");
    }
}

public class DeepLSettings
{
    public string ApiKey { get; set; } = default!;
    public string BaseUrl { get; set; } = "https://api.deepl.com/";
}

