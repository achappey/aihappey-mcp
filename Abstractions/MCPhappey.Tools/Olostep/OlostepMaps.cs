using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Olostep;

public static class OlostepMaps
{
    [Description("Create or continue an Olostep website map and return discovered URLs with cursor-based pagination support.")]
    [McpServerTool(Title = "Olostep create map", Name = "olostep_map_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Map_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Website URL to map. Optional when continuing with a cursor from a previous map response.")] string? url = null,
        [Description("Optional search query used to rank or filter the discovered links.")] string? search_query = null,
        [Description("Optional number limiting results to the most relevant top N links.")] int? top_n = null,
        [Description("Include subdomains for the provided website.")] bool include_subdomain = true,
        [Description("Include URL path patterns as newline-separated or comma-separated glob strings.")] string? include_urls = null,
        [Description("Exclude URL path patterns as newline-separated or comma-separated glob strings.")] string? exclude_urls = null,
        [Description("Pagination cursor returned by a previous map response.")] string? cursor = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepCreateMapRequest
                {
                    Url = url,
                    SearchQuery = search_query,
                    TopN = top_n,
                    IncludeSubdomain = include_subdomain,
                    IncludeUrls = include_urls,
                    ExcludeUrls = exclude_urls,
                    Cursor = cursor
                },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(typed.Url) && string.IsNullOrWhiteSpace(typed.Cursor))
                throw new ValidationException("Provide a url to start a map or a cursor to continue a previous map response.");

            var includePatterns = OlostepHelpers.ParseDelimitedList(typed.IncludeUrls);
            var excludePatterns = OlostepHelpers.ParseDelimitedList(typed.ExcludeUrls);

            var payload = new JsonObject();
            OlostepHelpers.AddIfNotNull(payload, "url", typed.Url);
            OlostepHelpers.AddIfNotNull(payload, "search_query", typed.SearchQuery);
            OlostepHelpers.AddIfNotNull(payload, "top_n", typed.TopN);
            payload["include_subdomain"] = typed.IncludeSubdomain;
            OlostepHelpers.AddIfNotNull(payload, "include_urls", includePatterns);
            OlostepHelpers.AddIfNotNull(payload, "exclude_urls", excludePatterns);
            OlostepHelpers.AddIfNotNull(payload, "cursor", typed.Cursor);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.PostJsonAsync("v1/maps", payload, cancellationToken) ?? new JsonObject();
            var mapId = OlostepHelpers.GetString(response, "id");
            var urlsCount = response["urls_count"]?.GetValue<int?>() ?? OlostepHelpers.CountArray(response["urls"]);
            var nextCursor = OlostepHelpers.GetString(response, "cursor");
            var summary = $"Olostep map completed. Id={mapId ?? "unknown"}. Urls={urlsCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/maps",
                    payload,
                    response,
                    ("id", mapId),
                    ("urlsCount", urlsCount),
                    ("cursor", nextCursor)),
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Retrieve a previously completed Olostep map by map id.")]
    [McpServerTool(Title = "Olostep get map", Name = "olostep_map_get", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Map_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Map identifier returned by Olostep map creation.")] string map_id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepGetMapRequest
                {
                    MapId = map_id
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.MapId);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.GetJsonAsync($"v1/maps/{Uri.EscapeDataString(typed.MapId)}", null, cancellationToken) ?? new JsonObject();
            var urlsCount = response["urls_count"]?.GetValue<int?>() ?? OlostepHelpers.CountArray(response["urls"]);
            var summary = $"Olostep map retrieved. Id={typed.MapId}. Urls={urlsCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/maps/{map_id}",
                    new { map_id = typed.MapId },
                    response,
                    ("id", OlostepHelpers.GetString(response, "id") ?? typed.MapId),
                    ("urlsCount", urlsCount),
                    ("cursor", OlostepHelpers.GetString(response, "cursor"))),
                Content = [summary.ToTextContentBlock()]
            };
        });
}

public sealed class OlostepCreateMapRequest
{
    [JsonPropertyName("url")]
    [Description("Website URL to map. Optional when continuing with a cursor.")]
    public string? Url { get; set; }

    [JsonPropertyName("search_query")]
    [Description("Optional search query used to rank returned URLs.")]
    public string? SearchQuery { get; set; }

    [JsonPropertyName("top_n")]
    [Range(1, int.MaxValue)]
    [Description("Optional limit for the top N most relevant URLs.")]
    public int? TopN { get; set; }

    [JsonPropertyName("include_subdomain")]
    [Description("Include subdomains for the target website.")]
    public bool IncludeSubdomain { get; set; } = true;

    [JsonPropertyName("include_urls")]
    [Description("Include URL path patterns as a newline-separated or comma-separated string.")]
    public string? IncludeUrls { get; set; }

    [JsonPropertyName("exclude_urls")]
    [Description("Exclude URL path patterns as a newline-separated or comma-separated string.")]
    public string? ExcludeUrls { get; set; }

    [JsonPropertyName("cursor")]
    [Description("Pagination cursor returned by a previous map response.")]
    public string? Cursor { get; set; }
}

public sealed class OlostepGetMapRequest
{
    [JsonPropertyName("map_id")]
    [Required]
    [Description("Map identifier returned by Olostep map creation.")]
    public string MapId { get; set; } = string.Empty;
}
