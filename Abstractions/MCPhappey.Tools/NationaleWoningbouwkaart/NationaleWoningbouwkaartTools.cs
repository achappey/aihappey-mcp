using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.NationaleWoningbouwkaart;

public static class NationaleWoningbouwkaartTools
{
    [Description("Get the complete Nationale Woningbouwkaart project list as lightweight summaries with only name and generated description text.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart list projects", Name = "nationale_woningbouwkaart_list_projects", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ListProjects(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = CreateClient(serviceProvider);
                var dataset = await client.GetProjectDatasetAsync(cancellationToken);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["totalProjects"] = dataset.Items.Count,
                    ["projects"] = CreateJsonArray(dataset.Items.Select(static item => item.ToListJson()))
                };
            }));

    [Description("Search Nationale Woningbouwkaart projects across naam, plannaam, municipality, province, plan status, and peilmoment. Returns up to 25 summarized matches.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart search projects", Name = "nationale_woningbouwkaart_search_projects", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> SearchProjects(
        [Description("Free-text query matched against naam, Plannaam, gemeente_naam, provincie_naam, Planstatus, and peilmoment.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of matches to return. Hard-capped at 25.")] int maxResults = 25,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var client = CreateClient(serviceProvider);
                var dataset = await client.GetProjectDatasetAsync(cancellationToken);
                var matches = SearchDataset(dataset, query, maxResults, CalculateProjectScore);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["query"] = query,
                    ["totalProjects"] = dataset.Items.Count,
                    ["matchCount"] = matches.Length,
                    ["matches"] = CreateJsonArray(matches.Select(static item => item.ToSearchJson()))
                };
            }));

    [Description("Get the complete Nationale Woningbouwkaart gemeente list as lightweight summaries with only name and generated description text.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart list gemeenten", Name = "nationale_woningbouwkaart_list_gemeenten", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ListMunicipalities(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = CreateClient(serviceProvider);
                var dataset = await client.GetMunicipalityDatasetAsync(cancellationToken);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["totalMunicipalities"] = dataset.Items.Count,
                    ["municipalities"] = CreateJsonArray(dataset.Items.Select(static item => item.ToListJson()))
                };
            }));

    [Description("Search Nationale Woningbouwkaart gemeenten across gemeente_naam, provincie_naam, gemeente_code, woondeal_regio, and peilmoment. Returns up to 25 summarized matches.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart search gemeenten", Name = "nationale_woningbouwkaart_search_gemeenten", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> SearchMunicipalities(
        [Description("Free-text query matched against gemeente_naam, provincie_naam, gemeente_code, woondeal_regio, and peilmoment.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of matches to return. Hard-capped at 25.")] int maxResults = 25,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var client = CreateClient(serviceProvider);
                var dataset = await client.GetMunicipalityDatasetAsync(cancellationToken);
                var matches = SearchDataset(dataset, query, maxResults, CalculateMunicipalityScore);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["query"] = query,
                    ["totalMunicipalities"] = dataset.Items.Count,
                    ["matchCount"] = matches.Length,
                    ["matches"] = CreateJsonArray(matches.Select(static item => item.ToSearchJson()))
                };
            }));

    [Description("Get the complete Nationale Woningbouwkaart woondeal region list as lightweight summaries with only name and generated description text.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart list woondeals", Name = "nationale_woningbouwkaart_list_woondeals", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> ListWoondeals(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var client = CreateClient(serviceProvider);
                var dataset = await client.GetWoondealDatasetAsync(cancellationToken);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["totalRegions"] = dataset.Items.Count,
                    ["regions"] = CreateJsonArray(dataset.Items.Select(static item => item.ToListJson()))
                };
            }));

    [Description("Search Nationale Woningbouwkaart woondeal regions across woondeal_regio and peilmoment. Returns up to 25 summarized matches.")]
    [McpServerTool(Title = "NationaleWoningbouwkaart search woondeals", Name = "nationale_woningbouwkaart_search_woondeals", ReadOnly = true, OpenWorld = true)]
    public static async Task<CallToolResult?> SearchWoondeals(
        [Description("Free-text query matched against woondeal_regio and peilmoment.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Maximum number of matches to return. Hard-capped at 25.")] int maxResults = 25,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(query);

                var client = CreateClient(serviceProvider);
                var dataset = await client.GetWoondealDatasetAsync(cancellationToken);
                var matches = SearchDataset(dataset, query, maxResults, CalculateWoondealScore);

                return new JsonObject
                {
                    ["source"] = BuildSource(dataset),
                    ["query"] = query,
                    ["totalRegions"] = dataset.Items.Count,
                    ["matchCount"] = matches.Length,
                    ["matches"] = CreateJsonArray(matches.Select(static item => item.ToSearchJson()))
                };
            }));

    private static NationaleWoningbouwkaartClient CreateClient(IServiceProvider serviceProvider)
        => new(serviceProvider.GetRequiredService<IHttpClientFactory>());

    private static JsonObject BuildSource(NationaleWoningbouwkaartDataset dataset) => new()
    {
        ["name"] = "NationaleWoningbouwkaart",
        ["dataset"] = dataset.DatasetName,
        ["url"] = dataset.SourceUrl,
        ["cachedAtUtc"] = dataset.CachedAtUtc,
        ["cacheExpiresAtUtc"] = dataset.ExpiresAtUtc
    };

    private static NationaleWoningbouwkaartItem[] SearchDataset(
        NationaleWoningbouwkaartDataset dataset,
        string query,
        int maxResults,
        Func<NationaleWoningbouwkaartItem, string, IReadOnlyList<string>, int> scoreSelector)
    {
        var normalizedQuery = Normalize(query);
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cappedMaxResults = Math.Clamp(maxResults, 1, 25);

        return dataset.Items
            .Select(item => new { Item = item, Score = scoreSelector(item, normalizedQuery, queryTokens) })
            .Where(static entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(cappedMaxResults)
            .Select(entry => entry.Item)
            .ToArray();
    }

    private static int CalculateProjectScore(NationaleWoningbouwkaartItem item, string normalizedQuery, IReadOnlyList<string> queryTokens)
    {
        if (!ContainsAllTokens(item.SearchTextNormalized, queryTokens))
            return 0;

        var score = 0;

        score += ScoreField(item.NameNormalized, normalizedQuery, exact: 500, startsWith: 350, contains: 250);
        score += ScoreField(item.AlternativeNameNormalized, normalizedQuery, exact: 400, startsWith: 275, contains: 200);
        score += ScoreField(item.MunicipalityNormalized, normalizedQuery, exact: 250, startsWith: 175, contains: 125);
        score += ScoreField(item.ProvinceNormalized, normalizedQuery, exact: 220, startsWith: 150, contains: 110);
        score += ScoreField(item.StatusNormalized, normalizedQuery, exact: 140, startsWith: 100, contains: 80);
        score += ScoreField(item.PeilmomentNormalized, normalizedQuery, exact: 120, startsWith: 90, contains: 70);

        foreach (var token in queryTokens.Distinct(StringComparer.Ordinal))
        {
            score += ScoreToken(item.NameNormalized, token, 60);
            score += ScoreToken(item.AlternativeNameNormalized, token, 45);
            score += ScoreToken(item.MunicipalityNormalized, token, 30);
            score += ScoreToken(item.ProvinceNormalized, token, 25);
            score += ScoreToken(item.StatusNormalized, token, 20);
            score += ScoreToken(item.PeilmomentNormalized, token, 15);
        }

        return queryTokens.Count > 1 ? score + 25 : score;
    }

    private static int CalculateMunicipalityScore(NationaleWoningbouwkaartItem item, string normalizedQuery, IReadOnlyList<string> queryTokens)
    {
        if (!ContainsAllTokens(item.SearchTextNormalized, queryTokens))
            return 0;

        var score = 0;

        score += ScoreField(item.NameNormalized, normalizedQuery, exact: 500, startsWith: 350, contains: 250);
        score += ScoreField(item.CodeNormalized, normalizedQuery, exact: 425, startsWith: 300, contains: 225);
        score += ScoreField(item.RegionNormalized, normalizedQuery, exact: 275, startsWith: 200, contains: 150);
        score += ScoreField(item.ProvinceNormalized, normalizedQuery, exact: 220, startsWith: 150, contains: 110);
        score += ScoreField(item.PeilmomentNormalized, normalizedQuery, exact: 120, startsWith: 90, contains: 70);

        foreach (var token in queryTokens.Distinct(StringComparer.Ordinal))
        {
            score += ScoreToken(item.NameNormalized, token, 60);
            score += ScoreToken(item.CodeNormalized, token, 45);
            score += ScoreToken(item.RegionNormalized, token, 35);
            score += ScoreToken(item.ProvinceNormalized, token, 25);
            score += ScoreToken(item.PeilmomentNormalized, token, 15);
        }

        return queryTokens.Count > 1 ? score + 25 : score;
    }

    private static int CalculateWoondealScore(NationaleWoningbouwkaartItem item, string normalizedQuery, IReadOnlyList<string> queryTokens)
    {
        if (!ContainsAllTokens(item.SearchTextNormalized, queryTokens))
            return 0;

        var score = 0;

        score += ScoreField(item.NameNormalized, normalizedQuery, exact: 500, startsWith: 350, contains: 250);
        score += ScoreField(item.PeilmomentNormalized, normalizedQuery, exact: 120, startsWith: 90, contains: 70);

        foreach (var token in queryTokens.Distinct(StringComparer.Ordinal))
        {
            score += ScoreToken(item.NameNormalized, token, 60);
            score += ScoreToken(item.PeilmomentNormalized, token, 20);
        }

        return queryTokens.Count > 1 ? score + 20 : score;
    }

    private static bool ContainsAllTokens(string searchText, IReadOnlyList<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return false;

        return queryTokens.Count == 0 || queryTokens.All(token => searchText.Contains(token, StringComparison.Ordinal));
    }

    private static JsonArray CreateJsonArray(IEnumerable<JsonObject> items)
        => new(items.Select(static item => (JsonNode)item).ToArray());

    private static int ScoreField(string? value, string query, int exact, int startsWith, int contains)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
            return 0;

        if (string.Equals(value, query, StringComparison.Ordinal))
            return exact;

        if (value.StartsWith(query, StringComparison.Ordinal))
            return startsWith;

        return value.Contains(query, StringComparison.Ordinal) ? contains : 0;
    }

    private static int ScoreToken(string? value, string token, int contains)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
            return 0;

        return value.Contains(token, StringComparison.Ordinal) ? contains : 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        var withoutDiacritics = builder.ToString().Normalize(NormalizationForm.FormC);
        return string.Join(' ', withoutDiacritics.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
