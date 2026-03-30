using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Olostep;

public static class OlostepSearch
{
    [Description("Search the web with Olostep and return a deduplicated set of relevant links with titles and descriptions.")]
    [McpServerTool(Title = "Olostep create search", Name = "olostep_search_create", Destructive = false, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Search_Create(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Natural language web search query.")] string query,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepCreateSearchRequest
                {
                    Query = query
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.Query);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var payload = new JsonObject
            {
                ["query"] = typed.Query
            };

            var response = await client.PostJsonAsync("v1/searches", payload, cancellationToken) ?? new JsonObject();
            var searchId = OlostepHelpers.GetString(response, "id");
            var linksCount = OlostepHelpers.CountArray(response["result"]?["links"]);
            var summary = $"Olostep search completed. Id={searchId ?? "unknown"}. Links={linksCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/searches",
                    payload,
                    response,
                    ("id", searchId),
                    ("query", typed.Query),
                    ("linksCount", linksCount)).ToJsonElement(),
                Content = [summary.ToTextContentBlock()]
            };
        });

    [Description("Retrieve a previously completed Olostep search by its search id.")]
    [McpServerTool(Title = "Olostep get search", Name = "olostep_search_get", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> Olostep_Search_Get(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Search identifier returned by Olostep search creation.")] string search_id,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var (typed, _, _) = await requestContext.Server.TryElicit(
                new OlostepGetSearchRequest
                {
                    SearchId = search_id
                },
                cancellationToken);

            ArgumentException.ThrowIfNullOrWhiteSpace(typed.SearchId);

            var client = serviceProvider.GetRequiredService<OlostepClient>();
            var response = await client.GetJsonAsync($"v1/searches/{Uri.EscapeDataString(typed.SearchId)}", null, cancellationToken) ?? new JsonObject();
            var linksCount = OlostepHelpers.CountArray(response["result"]?["links"]);
            var summary = $"Olostep search retrieved. Id={typed.SearchId}. Links={linksCount}.";

            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = OlostepHelpers.CreateStructuredResponse(
                    "/v1/searches/{search_id}",
                    new { search_id = typed.SearchId },
                    response,
                    ("id", OlostepHelpers.GetString(response, "id") ?? typed.SearchId),
                    ("query", OlostepHelpers.GetString(response, "query")),
                    ("linksCount", linksCount)).ToJsonElement(),
                Content = [summary.ToTextContentBlock()]
            };
        });
}

public sealed class OlostepCreateSearchRequest
{
    [JsonPropertyName("query")]
    [Required]
    [Description("Natural language web search query.")]
    public string Query { get; set; } = string.Empty;
}

public sealed class OlostepGetSearchRequest
{
    [JsonPropertyName("search_id")]
    [Required]
    [Description("Search identifier returned by Olostep search creation.")]
    public string SearchId { get; set; } = string.Empty;
}
