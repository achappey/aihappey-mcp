using System.Net.Http.Json;
using System.Text.Json;

namespace MCPhappey.Tools.OpenAI.Decisions;

/// <summary>Non-streaming Decisions client that preserves the complete API response.</summary>
public sealed class OpenAIDecisionsClient(HttpClient httpClient)
{
    public const string DefaultModel = "gpt-6-luna";

    public async Task<JsonElement> CreateAsync(OpenAIDecisionsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OpenAIDecisionsQuestions.Validate(request.Questions);

        using var response = await httpClient.PostAsJsonAsync("decisions", request, JsonSerializerOptions.Web, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"OpenAI Decisions API failed ({(int)response.StatusCode} {response.ReasonPhrase}): {GetErrorMessage(payload)}",
                null, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("answers", out var answers)
            || answers.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The OpenAI Decisions API returned an invalid decision object.");

        return document.RootElement.Clone();
    }

    private static string GetErrorMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } text)
                return text;
        }
        catch (JsonException)
        {
            // Do not return arbitrary HTML/error bodies to the caller.
        }
        return "The request was rejected without a readable error message.";
    }
}
