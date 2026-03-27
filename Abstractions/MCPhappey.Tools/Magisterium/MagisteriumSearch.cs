using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Magisterium;

public static class MagisteriumSearch
{
    private const string BaseUrl = "https://www.magisterium.com";
    private const string SearchEndpoint = "/api/v1/search";

    [Description("Search Catholic sources using Magisterium AI and return structured content.")]
    [McpServerTool(Title = "Magisterium search", Name = "magisterium_search", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Search(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Natural language query string.")] string query,
        [Description("Number of search results to return (1-100).")]
        int? numResults = null,
        [Description("Source category filter: auto, magisterial, or scholarly.")]
        string? category = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                if (numResults is < 1 or > 100)
                    throw new ArgumentOutOfRangeException(nameof(numResults), "numResults must be between 1 and 100.");

                if (!string.IsNullOrWhiteSpace(category))
                {
                    var normalizedCategory = category.Trim().ToLowerInvariant();
                    if (normalizedCategory is not ("auto" or "magisterial" or "scholarly"))
                        throw new ArgumentException("category must be one of: auto, magisterial, scholarly.", nameof(category));

                    category = normalizedCategory;
                }

                var settings = serviceProvider.GetRequiredService<MagisteriumSettings>();
                var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

                var payload = new JsonObject
                {
                    ["query"] = query
                };

                if (numResults.HasValue)
                    payload["numResults"] = numResults.Value;

                if (!string.IsNullOrWhiteSpace(category))
                    payload["category"] = category;

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{SearchEndpoint}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using var client = clientFactory.CreateClient();
                using var response = await client.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Magisterium search failed with {(int)response.StatusCode} {response.ReasonPhrase}: {raw}");

                return new JsonObject
                {
                    ["provider"] = "magisterium",
                    ["baseUrl"] = BaseUrl,
                    ["endpoint"] = SearchEndpoint,
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
