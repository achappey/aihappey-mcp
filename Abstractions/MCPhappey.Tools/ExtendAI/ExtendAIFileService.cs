using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using MCPhappey.Common.Models;
using System.Net.Mime;

namespace MCPhappey.Tools.ExtendAI;

public class ExtendAIFileService
{
    private const string DefaultUploadFilename = "document.bin";
    private readonly ExtendAIClient _client;

    public ExtendAIFileService(ExtendAIClient client)
    {
        _client = client;
    }

    public async Task<string> UploadAsync(FileItem file, CancellationToken cancellationToken = default)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(file.Contents.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/octet-stream");

        form.Add(content, "file", file.Filename ?? DefaultUploadFilename);

        using var request = new HttpRequestMessage(HttpMethod.Post, "files/upload")
        {
            Content = form
        };

        var response = await _client.SendAsync(request, cancellationToken)
            ?? throw new Exception("Extend file upload returned empty response.");

        var fileId = response["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(fileId))
            throw new Exception("Extend file upload response missing file id.");

        return fileId;
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"files/{Uri.EscapeDataString(fileId)}");
        await _client.SendAsync(request, cancellationToken);
    }

    public async Task<JsonNode> CreateParseRunAsync(object body, CancellationToken cancellationToken = default)
    {
        var response = await _client.PostJsonAsync("parse_runs", body, cancellationToken)
            ?? throw new Exception("Extend parse run response was empty.");

        return response;
    }

    public async Task<JsonNode> GetParseRunAsync(string parseRunId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"parse_runs/{Uri.EscapeDataString(parseRunId)}");
        var response = await _client.SendAsync(request, cancellationToken)
            ?? throw new Exception("Extend parse run response was empty.");

        return response;
    }

    public async Task DeleteParseRunAsync(string parseRunId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parseRunId)) return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"parse_runs/{Uri.EscapeDataString(parseRunId)}");
        await _client.SendAsync(request, cancellationToken);
    }

    public async Task<JsonNode> CreateExtractRunAsync(object body, CancellationToken cancellationToken = default)
    {
        var response = await _client.PostJsonAsync("extract_runs", body, cancellationToken)
            ?? throw new Exception("Extend extract run response was empty.");

        return response;
    }

    public async Task<JsonNode> GetExtractRunAsync(string extractRunId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"extract_runs/{Uri.EscapeDataString(extractRunId)}");
        var response = await _client.SendAsync(request, cancellationToken)
            ?? throw new Exception("Extend extract run response was empty.");

        return response;
    }

    public async Task DeleteExtractRunAsync(string extractRunId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extractRunId)) return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"extract_runs/{Uri.EscapeDataString(extractRunId)}");
        await _client.SendAsync(request, cancellationToken);
    }

    public async Task<JsonNode> CreateEditRunAsync(object body, CancellationToken cancellationToken = default)
    {
        var response = await _client.PostJsonAsync("edit_runs", body, cancellationToken)
            ?? throw new Exception("Extend edit run response was empty.");

        return response;
    }

    public async Task<JsonNode> GetEditRunAsync(string editRunId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"edit_runs/{Uri.EscapeDataString(editRunId)}");
        var response = await _client.SendAsync(request, cancellationToken)
            ?? throw new Exception("Extend edit run response was empty.");

        return response;
    }

    public async Task DeleteEditRunAsync(string editRunId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(editRunId)) return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"edit_runs/{Uri.EscapeDataString(editRunId)}");
        await _client.SendAsync(request, cancellationToken);
    }

    public async Task<JsonNode> CreateSplitRunAsync(object body, CancellationToken cancellationToken = default)
    {
        var response = await _client.PostJsonAsync("split_runs", body, cancellationToken)
            ?? throw new Exception("Extend split run response was empty.");

        return response;
    }

    public async Task<JsonNode> GetSplitRunAsync(string splitRunId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"split_runs/{Uri.EscapeDataString(splitRunId)}");
        var response = await _client.SendAsync(request, cancellationToken)
            ?? throw new Exception("Extend split run response was empty.");

        return response;
    }

    public async Task DeleteSplitRunAsync(string splitRunId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(splitRunId)) return;

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"split_runs/{Uri.EscapeDataString(splitRunId)}");
        await _client.SendAsync(request, cancellationToken);
    }

    public async Task<FileItem> DownloadFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new ArgumentException("fileId is required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"files/{Uri.EscapeDataString(fileId)}/content");
        using var response = await _client.RawSendAsync(request, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? MediaTypeNames.Application.Octet;
        var filename = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var fallbackName = string.IsNullOrWhiteSpace(filename) ? $"{fileId}" : filename;

        return new FileItem
        {
            Contents = BinaryData.FromBytes(bytes),
            Filename = fallbackName,
            MimeType = contentType,
            Uri = $"extendai://files/{fileId}"
        };
    }
}
