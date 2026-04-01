using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.BlinkUtilities;

public static class BlinkUtilitiesService
{
    private const string BaseUrl = "https://core.blink.new";

    [Description("Search the web via Blink Utilities and return structured organic results.")]
    [McpServerTool(
        Title = "Blink web search",
        Name = "blink_web_search",
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true)]
    public static async Task<CallToolResult?> Blink_WebSearch(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search query string.")] string query,
        [Description("Number of results to return (1-10). Default: 5.")] int? count = null,
        CancellationToken cancellationToken = default)
        => await PostEndpoint(
            serviceProvider,
            requestContext,
            endpoint: "/api/v1/search",
            payloadBuilder: payload =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                if (count is < 1 or > 10)
                    throw new ArgumentOutOfRangeException(nameof(count), "count must be between 1 and 10.");

                payload["query"] = query;

                if (count.HasValue)
                    payload["count"] = count.Value;
            },
            cancellationToken);

    [Description("Proxy an outbound HTTP request through Blink Utilities and return structured response details.")]
    [McpServerTool(
        Title = "Blink HTTP proxy",
        Name = "blink_http_proxy",
        Idempotent = false,
        OpenWorld = true,
        ReadOnly = false)]
    public static async Task<CallToolResult?> Blink_HttpProxy(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Target URL (must be public).")]
        string url,
        [Description("HTTP method. Default: GET. Allowed: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS.")]
        string? method = null,
        [Description("Custom request headers as JSON string, e.g. {\"X-API-Key\":\"your-key\"}.")]
        string? headersJson = null,
        [Description("Request body as raw string.")]
        string? body = null,
        CancellationToken cancellationToken = default)
        => await PostEndpoint(
            serviceProvider,
            requestContext,
            endpoint: "/api/v1/fetch",
            payloadBuilder: payload =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(url);

                payload["url"] = url;

                var normalizedMethod = string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant();
                var allowedMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
                if (!allowedMethods.Contains(normalizedMethod, StringComparer.Ordinal))
                    throw new ArgumentException("method must be one of: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS.", nameof(method));

                payload["method"] = normalizedMethod;

                if (!string.IsNullOrWhiteSpace(headersJson))
                {
                    var headersNode = JsonNode.Parse(headersJson)
                        ?? throw new ArgumentException("headersJson must be valid JSON object.", nameof(headersJson));

                    if (headersNode is not JsonObject headersObject)
                        throw new ArgumentException("headersJson must be a JSON object.", nameof(headersJson));

                    payload["headers"] = headersObject;
                }

                if (!string.IsNullOrEmpty(body))
                    payload["body"] = body;
            },
            cancellationToken);

    private static async Task<CallToolResult?> PostEndpoint(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string endpoint,
        Action<JsonObject> payloadBuilder,
        CancellationToken cancellationToken)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var settings = serviceProvider.GetRequiredService<BlinkUtilitiesSettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = new JsonObject();
                payloadBuilder(payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{endpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using var client = clientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Blink Utilities {endpoint} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {raw}");

                return new JsonObject
                {
                    ["provider"] = "blink-utilities",
                    ["baseUrl"] = BaseUrl,
                    ["endpoint"] = endpoint,
                    ["request"] = payload,
                    ["statusCode"] = (int)response.StatusCode,
                    ["response"] = TryParseJson(raw)
                };
            }));

    private static JsonNode TryParseJson(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) ?? JsonValue.Create(raw)!;
        }
        catch
        {
            return JsonValue.Create(raw)!;
        }
    }
}

