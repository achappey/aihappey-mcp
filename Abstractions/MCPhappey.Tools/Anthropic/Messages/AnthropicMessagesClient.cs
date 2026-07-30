using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Anthropic.Messages;

/// <summary>Minimal non-streaming client for Anthropic's Messages and Files APIs.</summary>
public sealed class AnthropicMessagesClient(HttpClient httpClient)
{
    public const string DefaultModel = "claude-haiku-4-5-20251001";
    public const string CodeExecutionBeta = "code-execution-2025-08-25";
    public const string FilesBeta = "files-api-2025-04-14";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<JsonObject> CreateMessageAsync(
        JsonObject request,
        IEnumerable<string>? betaFeatures = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddBetaFeatures(message, betaFeatures);
        return await SendJsonAsync(message, "Anthropic Messages API", cancellationToken);
    }

    public async Task<AnthropicFile> DownloadFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        var escapedId = Uri.EscapeDataString(fileId);

        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, $"v1/files/{escapedId}");
        AddBetaFeatures(metadataRequest, [FilesBeta]);
        var metadata = await SendJsonAsync(metadataRequest, "Anthropic Files API", cancellationToken);

        using var contentRequest = new HttpRequestMessage(HttpMethod.Get, $"v1/files/{escapedId}/content");
        AddBetaFeatures(contentRequest, [FilesBeta]);
        using var response = await httpClient.SendAsync(contentRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw CreateApiException("Anthropic Files API", response, error);
        }

        return new AnthropicFile(
            fileId,
            metadata["filename"]?.GetValue<string>() ?? fileId,
            metadata["mime_type"]?.GetValue<string>()
                ?? response.Content.Headers.ContentType?.MediaType
                ?? "application/octet-stream",
            await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    public static string? GetText(JsonObject response)
    {
        var values = response["content"]?.AsArray()
            .OfType<JsonObject>()
            .Where(block => block["type"]?.GetValue<string>() == "text")
            .Select(block => block["text"]?.GetValue<string>())
            .Where(text => !string.IsNullOrWhiteSpace(text));
        var text = string.Join("\n\n", values ?? []);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static IReadOnlyList<string> GetGeneratedFileIds(JsonObject response)
        => Descendants(response)
            .OfType<JsonObject>()
            .Select(node => node["file_id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();

    private async Task<JsonObject> SendJsonAsync(
        HttpRequestMessage request,
        string apiName,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(apiName, response, payload);

        return JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException($"{apiName} returned an invalid JSON object.");
    }

    private static void AddBetaFeatures(HttpRequestMessage request, IEnumerable<string>? features)
    {
        var value = string.Join(',', features?.Where(feature => !string.IsNullOrWhiteSpace(feature)).Distinct() ?? []);
        if (!string.IsNullOrWhiteSpace(value))
            request.Headers.TryAddWithoutValidation(AnthropicHeaders.AnthropicBetaHeader, value);
    }

    private static HttpRequestException CreateApiException(string apiName, HttpResponseMessage response, string payload)
    {
        var message = "The request was rejected without a readable error message.";
        try
        {
            var json = JsonNode.Parse(payload);
            message = json?["error"]?["message"]?.GetValue<string>()
                ?? json?["message"]?.GetValue<string>()
                ?? message;
        }
        catch (JsonException) { }

        return new HttpRequestException(
            $"{apiName} failed ({(int)response.StatusCode} {response.ReasonPhrase}): {message}",
            null,
            response.StatusCode);
    }

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        if (node is JsonObject obj)
        {
            foreach (var child in obj.Select(property => property.Value).Where(value => value is not null))
                foreach (var descendant in Descendants(child!))
                    yield return descendant;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.Where(value => value is not null))
                foreach (var descendant in Descendants(child!))
                    yield return descendant;
        }
    }
}

public sealed record AnthropicFile(string Id, string Filename, string MimeType, byte[] Data);
