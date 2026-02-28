using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AIsa;

public static class AIsaScholar
{
    [Description("Perform academic paper search via AIsa POST /scholar/search/scholar.")]
    [McpServerTool(
        Name = "aisa_scholar_search_scholar",
        Title = "AIsa Scholar search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Scholar_Search_Scholar(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required search query for scholarly materials.")] string query,
        [Description("Maximum number of search results to return (1-100). Defaults to 10 by API.")]
        int? maxNumResults = null,
        [Description("Year of publication lower bound (1900-2030).")]
        int? asYlo = null,
        [Description("Year of publication upper bound (1900-2030).")]
        int? asYhi = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                ValidateRange(maxNumResults, 1, 100, nameof(maxNumResults));
                ValidateRange(asYlo, 1900, 2030, nameof(asYlo));
                ValidateRange(asYhi, 1900, 2030, nameof(asYhi));

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumResults,
                    ["as_ylo"] = asYlo,
                    ["as_yhi"] = asYhi
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("scholar/search/scholar", payload, cancellationToken);
            }));

    [Description("Perform web search and return structured results via AIsa POST /scholar/search/web.")]
    [McpServerTool(
        Name = "aisa_scholar_search_web",
        Title = "AIsa Scholar web search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Scholar_Search_Web(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required search query for scholarly materials.")] string query,
        [Description("Maximum number of search results to return (1-100). Defaults to 10 by API.")]
        int? maxNumResults = null,
        [Description("Year of publication lower bound (1900-2030).")]
        int? asYlo = null,
        [Description("Year of publication upper bound (1900-2030).")]
        int? asYhi = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                ValidateRange(maxNumResults, 1, 100, nameof(maxNumResults));
                ValidateRange(asYlo, 1900, 2030, nameof(asYlo));
                ValidateRange(asYhi, 1900, 2030, nameof(asYhi));

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumResults,
                    ["as_ylo"] = asYlo,
                    ["as_yhi"] = asYhi
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("scholar/search/web", payload, cancellationToken);
            }));

    [Description("Perform intelligent search combining web and academic results via AIsa POST /scholar/search/mixed.")]
    [McpServerTool(
        Name = "aisa_scholar_search_mixed",
        Title = "AIsa Scholar smart search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Scholar_Search_Mixed(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required search query for scholarly materials.")] string query,
        [Description("Maximum number of search results to return (1-100). Defaults to 10 by API.")]
        int? maxNumResults = null,
        [Description("Year of publication lower bound (1900-2030).")]
        int? asYlo = null,
        [Description("Year of publication upper bound (1900-2030).")]
        int? asYhi = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);
                ValidateRange(maxNumResults, 1, 100, nameof(maxNumResults));
                ValidateRange(asYlo, 1900, 2030, nameof(asYlo));
                ValidateRange(asYhi, 1900, 2030, nameof(asYhi));

                var payload = new JsonObject
                {
                    ["query"] = query,
                    ["max_num_results"] = maxNumResults,
                    ["as_ylo"] = asYlo,
                    ["as_yhi"] = asYhi
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("scholar/search/mixed", payload, cancellationToken);
            }));

    [Description("Generate explanations for scholar search results via AIsa POST /scholar/search/explain.")]
    [McpServerTool(
        Name = "aisa_scholar_search_explain",
        Title = "AIsa Scholar explain results",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Scholar_Search_Explain(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required ID of the search to explain.")] string searchId,
        [Description("Response mode: COMPLETE, SUMMARY, BULLET_POINTS.")]
        string? responseMode = null,
        [Description("Language code for the explanation response (for example: en, zh, ar).")]
        string? language = null,
        [Description("Detail level: BRIEF, MODERATE, DETAILED.")]
        string? detailLevel = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(searchId);

                var normalizedResponseMode = NormalizeEnumOrNull(
                    responseMode,
                    nameof(responseMode),
                    ["COMPLETE", "SUMMARY", "BULLET_POINTS"]);

                var normalizedDetailLevel = NormalizeEnumOrNull(
                    detailLevel,
                    nameof(detailLevel),
                    ["BRIEF", "MODERATE", "DETAILED"]);

                var payload = new JsonObject
                {
                    ["search_id"] = searchId,
                    ["response_mode"] = normalizedResponseMode,
                    ["language"] = language,
                    ["detail_level"] = normalizedDetailLevel
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("scholar/search/explain", payload, cancellationToken);
            }));

    private static void ValidateRange(int? value, int minInclusive, int maxInclusive, string parameterName)
    {
        if (!value.HasValue)
            return;

        if (value.Value < minInclusive || value.Value > maxInclusive)
            throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be between {minInclusive} and {maxInclusive}.");
    }

    private static string? NormalizeEnumOrNull(string? value, string parameterName, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToUpperInvariant();
        if (!allowed.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException($"{parameterName} must be one of: {string.Join(", ", allowed)}.", parameterName);

        return normalized;
    }
}
