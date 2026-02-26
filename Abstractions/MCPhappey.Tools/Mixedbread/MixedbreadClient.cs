using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Tools.Mixedbread;

internal static class MixedbreadConstants
{
    internal const string BaseUrl = "https://api.mixedbread.com";
    internal const string IconSource = "https://www.mixedbread.com/apple-touch-icon.png";
}

public sealed class MixedbreadSettings
{
    public string ApiKey { get; set; } = default!;
}

internal static class MixedbreadHttp
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static HttpClient CreateClient(IServiceProvider serviceProvider, MixedbreadSettings settings)
    {
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient();
        client.BaseAddress ??= new Uri(MixedbreadConstants.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));
        return client;
    }

    internal static StringContent CreateJsonContent(JsonObject payload)
        => new(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, MimeTypes.Json);

    internal static async Task<JsonNode> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"{response.StatusCode}: {text}");

        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        return JsonNode.Parse(text) ?? new JsonObject { ["content"] = text };
    }
}
