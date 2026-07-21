using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.OpenAI.Responses;

/// <summary>
/// Minimal HTTP client for non-streaming OpenAI Responses API calls.
/// This intentionally avoids the OpenAI SDK so connector requests retain their raw API shape.
/// </summary>
public sealed class OpenAIResponsesClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> CreateTextResponseAsync(
        OpenAIResponsesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await httpClient.PostAsJsonAsync(
            "responses",
            request,
            JsonOptions,
            cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI Responses API failed ({(int)response.StatusCode} {response.ReasonPhrase}): {GetErrorMessage(payload)}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(payload);
        var outputText = GetOutputText(document.RootElement);
        if (string.IsNullOrWhiteSpace(outputText))
            throw new InvalidOperationException("The OpenAI Responses API returned no text output.");

        return outputText;
    }

    private static string GetErrorMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } errorMessage)
            {
                return errorMessage;
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic message for non-JSON error bodies.
        }

        return "The request was rejected without a readable error message.";
    }

    private static string? GetOutputText(JsonElement response)
    {
        if (response.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!response.TryGetProperty("output", out var output)
            || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var itemText)
                    && itemText.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(itemText.GetString()))
                {
                    text.Add(itemText.GetString()!);
                }
            }
        }

        return text.Count == 0 ? null : string.Concat(text);
    }
}

public sealed class OpenAIResponsesRequest
{
    public string Model { get; init; } = "gpt-5.2";
    public required string Input { get; init; }
    public double Temperature { get; init; } = 1;
    public OpenAIReasoningOptions Reasoning { get; init; } = new();
    public required IReadOnlyList<OpenAIMcpTool> Tools { get; init; }
}

public sealed class OpenAIReasoningOptions
{
    public string Effort { get; init; } = "none";
}

public sealed class OpenAIMcpTool
{
    public string Type { get; init; } = "mcp";

    [JsonPropertyName("server_label")]
    public required string ServerLabel { get; init; }
    public required string Authorization { get; init; }
    
    [JsonPropertyName("connector_id")]
    public required string ConnectorId { get; init; }

    [JsonPropertyName("require_approval")]
    public string RequireApproval { get; init; } = "never";
}
