using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPhappey.Tools.Google.Interactions;

/// <summary>
/// Minimal non-streaming client for Google's Interactions API. The raw JSON
/// contract is intentional so this repository remains independent of AIHappey.
/// </summary>
public sealed class GoogleInteractionsClient(HttpClient httpClient)
{
    public const string DefaultModel = "gemini-flash-latest";
    public const string ApiRevision = "2026-05-20";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<JsonObject> CreateInteractionAsync(
        GoogleInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(HttpMethod.Post, "v1beta/interactions")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.TryAddWithoutValidation("Api-Revision", ApiRevision);

        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Google Interactions API failed ({(int)response.StatusCode} {response.ReasonPhrase}): {GetErrorMessage(payload)}",
                null,
                response.StatusCode);
        }

        return JsonNode.Parse(payload)?.AsObject()
            ?? throw new InvalidOperationException("Google Interactions API returned an invalid JSON object.");
    }

    public async Task<string> CreateTextInteractionAsync(
        GoogleInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        var interaction = await CreateInteractionAsync(request, cancellationToken);
        return GoogleInteractionResponse.GetText(interaction)
            ?? throw new InvalidOperationException("Google Interactions API returned no text output.");
    }

    private static string GetErrorMessage(string payload)
    {
        try
        {
            var root = JsonNode.Parse(payload);
            return root?["error"]?["message"]?.GetValue<string>()
                ?? root?["message"]?.GetValue<string>()
                ?? "The request was rejected without a readable error message.";
        }
        catch (JsonException)
        {
            return "The request was rejected without a readable error message.";
        }
    }
}

public sealed class GoogleInteractionRequest
{
    public string Model { get; init; } = GoogleInteractionsClient.DefaultModel;
    public required JsonNode Input { get; init; }
    public string? SystemInstruction { get; init; }
    public JsonArray? Tools { get; init; }
    public JsonObject? ResponseFormat { get; init; }
    public IReadOnlyList<string>? ResponseModalities { get; init; }
    public JsonObject? GenerationConfig { get; init; }
    public bool Store { get; init; } = false;
    public bool Stream { get; init; } = false;
}

public static class GoogleInteractionInput
{
    public static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    public static JsonObject Media(string type, string? data, string? uri, string? mimeType) => new()
    {
        ["type"] = type,
        ["data"] = data,
        ["uri"] = uri,
        ["mime_type"] = mimeType
    };

    public static JsonObject Bytes(string type, BinaryData data, string mimeType) =>
        Media(type, Convert.ToBase64String(data.ToArray()), null, mimeType);
}

public sealed record GoogleInteractionMedia(string Type, byte[] Data, string MimeType);

public static class GoogleInteractionResponse
{
    public static string? GetText(JsonObject interaction)
    {
        var text = Descendants(interaction)
            .OfType<JsonObject>()
            .Where(item => item["type"]?.GetValue<string>() == "text")
            .Select(item => item["text"]?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var result = string.Join("\n\n", text!);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public static IReadOnlyList<GoogleInteractionMedia> GetMedia(JsonObject interaction, params string[] acceptedTypes)
    {
        var types = acceptedTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<GoogleInteractionMedia>();

        foreach (var item in Descendants(interaction).OfType<JsonObject>())
        {
            var type = item["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(type) || !types.Contains(type))
                continue;

            var data = item["data"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(data))
                continue;

            var comma = data.IndexOf(',');
            if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
                data = data[(comma + 1)..];

            try
            {
                result.Add(new GoogleInteractionMedia(
                    type,
                    Convert.FromBase64String(data),
                    item["mime_type"]?.GetValue<string>() ?? DefaultMimeType(type)));
            }
            catch (FormatException)
            {
                // Ignore malformed media while retaining any valid output parts.
            }
        }

        return result;
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

    private static string DefaultMimeType(string type) => type.ToLowerInvariant() switch
    {
        "image" => "image/png",
        "audio" => "audio/L16;rate=24000",
        "video" => "video/mp4",
        _ => "application/octet-stream"
    };
}
