using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Tools.OpenAI.Skills;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Tools.OpenAI.AgentsApi;

internal static class OpenAIAgentsHttp
{
    internal const string ApiBaseUrl = "https://api.openai.com/v1";
    internal const string BetaHeaderName = "OpenAI-Beta";
    internal const string BetaHeaderValue = "agents=v1";

    internal static HttpClient CreateHttpClient(IServiceProvider serviceProvider)
    {
        var settings = serviceProvider.GetRequiredService<OpenAISettings>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("An OpenAI API key is required.");

        var client = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        client.DefaultRequestHeaders.TryAddWithoutValidation(BetaHeaderName, BetaHeaderValue);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    internal static async Task<JsonNode> SendAsync(
        IServiceProvider serviceProvider,
        HttpMethod method,
        string url,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        var client = CreateHttpClient(serviceProvider);
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI Agents API returned {(int)response.StatusCode} {response.StatusCode}: {responseText}");

        if (string.IsNullOrWhiteSpace(responseText))
            return new JsonObject();

        return JsonNode.Parse(responseText)
            ?? throw new ValidationException("The OpenAI Agents API response was not valid JSON.");
    }

    internal static async Task<JsonObject> GetObjectAsync(
        IServiceProvider serviceProvider,
        string url,
        CancellationToken cancellationToken)
        => await SendAsync(serviceProvider, HttpMethod.Get, url, null, cancellationToken) as JsonObject
            ?? throw new ValidationException($"Expected a JSON object from '{url}'.");

    internal static JsonObject ParseObject(string json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException($"{parameterName} is required.");

        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw new ValidationException($"{parameterName} must contain a JSON object.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ValidationException($"{parameterName} is not valid JSON: {exception.Message}");
        }
    }

    internal static JsonObject CloneObject(JsonNode? node)
        => node?.DeepClone() as JsonObject ?? new JsonObject();

    internal static JsonArray CloneArray(JsonNode? node)
        => node?.DeepClone() as JsonArray ?? new JsonArray();

    internal static JsonArray ToArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
            result.Add(value);
        return result;
    }

    internal static List<string> ParseDelimited(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToList();

    internal static void ValidateHttpsUrl(string value, string parameterName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException($"{parameterName} must be an absolute HTTPS URL.");
    }
}
