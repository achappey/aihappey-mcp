using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AIsa;

public static class AIsaFinancial
{
    [Description("Search financial line items via AIsa POST /financial/financials/search/line-items.")]
    [McpServerTool(
        Name = "aisa_financial_search_line_items",
        Title = "AIsa Financial line item search",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Financial_Search_Line_Items(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required line items as JSON array string, e.g. [\"revenue\",\"net_income\"].")]
        string lineItemsJson,
        [Description("Required tickers as JSON array string, e.g. [\"AAPL\",\"MSFT\"].")]
        string tickersJson,
        [Description("Time period: annual, quarterly, ttm. Defaults to ttm.")]
        string? period = null,
        [Description("Maximum number of results (>= 1).")]
        int? limit = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var lineItems = ParseRequiredStringArray(lineItemsJson, nameof(lineItemsJson));
                var tickers = ParseRequiredStringArray(tickersJson, nameof(tickersJson));

                var payload = new JsonObject
                {
                    ["line_items"] = lineItems,
                    ["tickers"] = tickers,
                    ["period"] = period,
                    ["limit"] = limit
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("financial/financials/search/line-items", payload, cancellationToken);
            }));

    [Description("Run financial stock screener via AIsa POST /financial/financials/search.")]
    [McpServerTool(
        Name = "aisa_financial_stock_screener",
        Title = "AIsa Financial stock screener",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> AIsa_Financial_Stock_Screener(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Required filters as JSON array string. Example: [{\"field\":\"market_cap\",\"operator\":\"gt\",\"value\":1000000000}].")]
        string filtersJson,
        [Description("Time period: annual, quarterly, ttm. Defaults to ttm.")]
        string? period = null,
        [Description("Maximum number of results (1-100).")]
        int? limit = null,
        [Description("Sort field. Allowed examples: ticker, -ticker, report_period, -report_period.")]
        string? orderBy = null,
        [Description("Optional currency enum value.")]
        string? currency = null,
        [Description("Whether to return historical data. Defaults to false.")]
        bool? historical = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                var filters = ParseRequiredArray(filtersJson, nameof(filtersJson));

                var payload = new JsonObject
                {
                    ["filters"] = filters,
                    ["period"] = period,
                    ["limit"] = limit,
                    ["order_by"] = orderBy,
                    ["currency"] = currency,
                    ["historical"] = historical
                };

                var client = serviceProvider.GetRequiredService<AIsaClient>();
                return await client.PostAsync("financial/financials/search", payload, cancellationToken);
            }));

    private static JsonArray ParseRequiredStringArray(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);

        JsonArray array;
        try
        {
            var node = JsonNode.Parse(value);
            if (node is not JsonArray jsonArray)
                throw new ArgumentException($"{parameterName} must be a JSON array.", parameterName);

            array = jsonArray;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }

        var result = new JsonArray();
        foreach (var item in array)
        {
            var text = item?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }

        if (result.Count == 0)
            throw new ArgumentException($"{parameterName} must contain at least one non-empty string.", parameterName);

        return result;
    }

    private static JsonArray ParseRequiredArray(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);

        try
        {
            var node = JsonNode.Parse(value);
            if (node is not JsonArray array)
                throw new ArgumentException($"{parameterName} must be a JSON array.", parameterName);

            if (array.Count == 0)
                throw new ArgumentException($"{parameterName} must contain at least one item.", parameterName);

            return array;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{parameterName} is not valid JSON.", parameterName, ex);
        }
    }
}

