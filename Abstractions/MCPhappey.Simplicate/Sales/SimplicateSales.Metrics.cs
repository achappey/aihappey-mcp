using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{
    [Description("Compute repeat-vs-new sales metrics (counts/amounts) based on Simplicate sales, per gekozen periode en definitie van 'huidige klant'.")]
    [McpServerTool(Title = "Get Simplicate repeat sales metrics", OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult> SimplicateSales_GetRepeatMetrics(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Date field to evaluate on. Default: ExpectedClosingDate.")] SalesDateField dateField = SalesDateField.ExpectedClosingDate,
        [Description("Start date (inclusive), yyyy-MM-dd.")] string fromDate = "",
        [Description("End date (inclusive), yyyy-MM-dd.")] string toDate = "",
        [Description("Status label to include (default: scored).")] string statusLabel = "scored",
        [Description("Responsible employee name (contains). Optional.")] string? responsibleEmployeeName = null,
        [Description("Organization name (contains). Optional.")] string? organisationName = null,
        [Description("Person full name (exact). Optional.")] string? personName = null,
        [Description("Team name (contains). Optional.")] string? teamName = null,
        [Description("Source name (exact). Optional.")] string? sourceName = null,
        [Description("Expected revenue >=. Optional.")] decimal? amountGe = null,
        [Description("Expected revenue <=. Optional.")] decimal? amountLe = null,
        [Description("Include rows for unknown organization (no organization on the sale). Default: false.")] bool includeUnknownOrganization = false,
        [Description("Definition of 'current customer': Historical (ever before fromDate) or RollingWindowMonths.")] RepeatCustomerDefinition definition = RepeatCustomerDefinition.Historical,
        [Description("Size of rolling window in months when definition=RollingWindowMonths. Default: 12.")] int rollingWindowMonths = 12,
        [Description("Earliest history date to fetch for determining 'existing customers' (yyyy-MM-dd). Optional; if omitted, uses 2019-01-01 or (fromDate minus rollingWindowMonths*2).")] string? historyFromDate = null,
        [Description("Return per-organization breakdown as well. Default: false.")] bool includePerOrganization = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fromDate) || string.IsNullOrWhiteSpace(toDate))
            throw new ArgumentException("fromDate and toDate are required (yyyy-MM-dd).");

        // Resolve dependencies
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // Date field mapping
        string dateFieldQuery = dateField switch
        {
            SalesDateField.CreatedAt => "created_at",
            SalesDateField.UpdatedAt => "updated_at",
            SalesDateField.StartDate => "start_date",
            _ => "expected_closing_date"
        };

        // History window to classify customers correctly
        // Historical: need 'ever before fromDate' -> fetch from historyFromDate up to toDate
        // Rolling: need only last N months before fromDate (but fetch a bit more margin)
        var historyDefault = definition == RepeatCustomerDefinition.Historical
            ? "2019-01-01"
            : ComputeDateMonthsBefore(fromDate, Math.Max(rollingWindowMonths * 2, 12)); // fetch sufficient rolling history

        var histFrom = string.IsNullOrWhiteSpace(historyFromDate) ? historyDefault : historyFromDate;

        // Build common filters
        var filters = new List<string>
        {
            $"q[{dateFieldQuery}][ge]={Uri.EscapeDataString(histFrom)}",
            $"q[{dateFieldQuery}][le]={Uri.EscapeDataString(toDate)}"
        };
        if (!string.IsNullOrWhiteSpace(statusLabel)) filters.Add($"q[status.label]={Uri.EscapeDataString(statusLabel)}");
        if (!string.IsNullOrWhiteSpace(responsibleEmployeeName)) filters.Add($"q[responsible_employee.name]=*{Uri.EscapeDataString(responsibleEmployeeName)}*");
        if (!string.IsNullOrWhiteSpace(organisationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organisationName)}*");
        if (!string.IsNullOrWhiteSpace(personName)) filters.Add($"q[person.full_name]={Uri.EscapeDataString(personName)}");
        if (!string.IsNullOrWhiteSpace(teamName)) filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");
        if (!string.IsNullOrWhiteSpace(sourceName)) filters.Add($"q[source.name]={Uri.EscapeDataString(sourceName)}");
        if (amountGe.HasValue) filters.Add($"q[expected_revenue][ge]={amountGe.Value}");
        if (amountLe.HasValue) filters.Add($"q[expected_revenue][le]={amountLe.Value}");

        // Minimal select — fields needed for grouping, amounts, and dating
        filters.Add("select=expected_revenue,organization.,status.,created_at,updated_at,start_date,expected_closing_date");

        string baseUrl = simplicateOptions.GetApiUrl("/sales/sales");
        string filterString = string.Join("&", filters);

        // Download all relevant sales once (history + analysis window)
        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateSalesItem>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading sales for repeat metrics (page {pageNum})",
            requestContext,
            cancellationToken: cancellationToken
        );

        // Select the date to use
        Func<SimplicateSalesItem, DateTime?> dateSelector = dateField switch
        {
            SalesDateField.CreatedAt => x => x.created_at,
            SalesDateField.UpdatedAt => x => x.updated_at,
            SalesDateField.StartDate => x => x.start_date,
            _ => x => x.expected_closing_date
        };

        // Prepare time bounds
        if (!DateTime.TryParse(fromDate, out var fromDt)) throw new ArgumentException("fromDate invalid");
        if (!DateTime.TryParse(toDate, out var toDt)) throw new ArgumentException("toDate invalid");
        if (!DateTime.TryParse(histFrom, out var histFromDt)) throw new ArgumentException("historyFromDate invalid");

        // Normalize items, optionally exclude unknown orgs
        var records = items
            .Where(x => includeUnknownOrganization || x.organization != null)
            .Select(x => new
            {
                OrgId = x.organization?.id ?? "unknown",
                OrgName = x.organization?.name ?? "(onbekend)",
                Dt = dateSelector(x),
                Amount = x.expected_revenue ?? 0m
            })
            .Where(x => x.Dt.HasValue)
            .ToList();

        // Group by org and sort by date to determine first-ever sale and rolling windows
        var byOrg = records
            .GroupBy(r => new { r.OrgId, r.OrgName })
            .Select(g => new
            {
                g.Key.OrgId,
                g.Key.OrgName,
                Sales = g.OrderBy(r => r.Dt!.Value).ToList()
            })
            .ToList();

        // Classify sales within the analysis window
        int newCount = 0, repeatCount = 0;
        decimal newAmount = 0m, repeatAmount = 0m;

        var perOrgList = includePerOrganization ? new List<RepeatOrgBreakdown>() : null;

        foreach (var org in byOrg)
        {
            // Determine "is existing customer before fromDate" depending on definition
            bool existingBeforeFrom =
                definition == RepeatCustomerDefinition.Historical
                    ? org.Sales.Any(s => s.Dt!.Value < fromDt)
                    : org.Sales.Any(s => s.Dt!.Value >= fromDt.AddMonths(-rollingWindowMonths) && s.Dt!.Value < fromDt);

            int orgNew = 0, orgRepeat = 0;
            decimal orgNewAmt = 0m, orgRepeatAmt = 0m;

            // Keep track of first-ever date (for in-period first/new logo identification under Historical)
            var firstEverDate = org.Sales.FirstOrDefault()?.Dt;

            foreach (var s in org.Sales)
            {
                var d = s.Dt!.Value;
                if (d < fromDt || d > toDt)
                    continue;

                bool isNew;

                if (definition == RepeatCustomerDefinition.Historical)
                {
                    // New if this sale is the first-ever for the org (i.e., date equals firstEverDate and falls within period)
                    isNew = firstEverDate.HasValue && d == firstEverDate.Value;
                }
                else
                {
                    // Rolling window: new if no sale in the rolling window BEFORE this sale
                    var windowStart = d.AddMonths(-rollingWindowMonths);
                    // Any prior sale in (windowStart, d)
                    bool hadRecentPrior = org.Sales.Any(p => p.Dt!.Value < d && p.Dt!.Value >= windowStart);
                    isNew = !hadRecentPrior;
                }

                // Historical nuance: if the org was existingBeforeFrom, then all in-period sales are repeat
                if (definition == RepeatCustomerDefinition.Historical && existingBeforeFrom)
                    isNew = false;

                if (isNew)
                {
                    newCount++;
                    newAmount += s.Amount;
                    orgNew++;
                    orgNewAmt += s.Amount;
                }
                else
                {
                    repeatCount++;
                    repeatAmount += s.Amount;
                    orgRepeat++;
                    orgRepeatAmt += s.Amount;
                }
            }

            if (includePerOrganization && (orgNew + orgRepeat) > 0)
            {
                perOrgList!.Add(new RepeatOrgBreakdown
                {
                    Organization = new OrganizationKey { id = org.OrgId, name = org.OrgName },
                    NewCount = orgNew,
                    NewAmount = orgNewAmt,
                    RepeatCount = orgRepeat,
                    RepeatAmount = orgRepeatAmt,
                    RepeatShareCount = (orgNew + orgRepeat) > 0 ? (double)orgRepeat / (orgNew + orgRepeat) : 0d,
                    RepeatShareAmount = (orgNewAmt + orgRepeatAmt) > 0 ? (double)(orgRepeatAmt / (orgNewAmt + orgRepeatAmt)) : 0d
                });
            }
        }

        int totalCount = newCount + repeatCount;
        decimal totalAmount = newAmount + repeatAmount;

        var result = new RepeatMetricsResult
        {
            Params = new RepeatParamsEcho
            {
                DateField = dateField.ToString(),
                FromDate = fromDate,
                ToDate = toDate,
                StatusLabel = statusLabel,
                IncludeUnknownOrganization = includeUnknownOrganization,
                Definition = definition.ToString(),
                RollingWindowMonths = rollingWindowMonths,
                HistoryFromDate = histFrom
            },
            Totals = new RepeatTotals
            {
                TotalCount = totalCount,
                TotalAmount = totalAmount,
                NewCount = newCount,
                NewAmount = newAmount,
                RepeatCount = repeatCount,
                RepeatAmount = repeatAmount,
                RepeatShareCount = totalCount > 0 ? (double)repeatCount / totalCount : 0d,
                RepeatShareAmount = totalAmount > 0 ? (double)(repeatAmount / totalAmount) : 0d
            },
            PerOrganization = includePerOrganization ? perOrgList : null,
            Meta = new
            {
                Source = $"{baseUrl}?{filterString}",
                Notes = new[]
                {
                    "Historical: Repeat = elke sale in de periode van organisaties die vóór fromDate al een scored sale hadden. Eerste sale ooit binnen de periode telt als 'New'.",
                    "RollingWindowMonths: Repeat = sale heeft een eerdere sale binnen N maanden voorafgaand aan de sale-datum.",
                    "Amount gebruikt expected_revenue.",
                    "Resultaat is gebaseerd op status.label filter (default 'scored')."
                }
            }
        };

        // JSON content block
        return result.ToJsonContentBlock($"{baseUrl}?{filterString}").ToCallToolResult();
    }

    // === Types voor deze tool ===
    public enum RepeatCustomerDefinition
    {
        Historical,          // 'Current customer' = had sale vóór fromDate
        RollingWindowMonths  // 'Current customer' = had sale binnen N maanden vóór sale
    }

    public sealed class RepeatMetricsResult
    {
        public RepeatParamsEcho Params { get; set; } = new RepeatParamsEcho();
        public RepeatTotals Totals { get; set; } = new RepeatTotals();
        public List<RepeatOrgBreakdown>? PerOrganization { get; set; }
        public object? Meta { get; set; }
    }

    public sealed class RepeatParamsEcho
    {
        public string DateField { get; set; } = "";
        public string FromDate { get; set; } = "";
        public string ToDate { get; set; } = "";
        public string StatusLabel { get; set; } = "";
        public bool IncludeUnknownOrganization { get; set; }
        public string Definition { get; set; } = "";
        public int RollingWindowMonths { get; set; }
        public string HistoryFromDate { get; set; } = "";
    }

    public sealed class RepeatTotals
    {
        public int TotalCount { get; set; }
        public decimal TotalAmount { get; set; }
        public int NewCount { get; set; }
        public decimal NewAmount { get; set; }
        public int RepeatCount { get; set; }
        public decimal RepeatAmount { get; set; }
        public double RepeatShareCount { get; set; }
        public double RepeatShareAmount { get; set; }
    }

    public sealed class RepeatOrgBreakdown
    {
        public OrganizationKey Organization { get; set; } = new OrganizationKey();
        public int NewCount { get; set; }
        public decimal NewAmount { get; set; }
        public int RepeatCount { get; set; }
        public decimal RepeatAmount { get; set; }
        public double RepeatShareCount { get; set; }
        public double RepeatShareAmount { get; set; }
    }

    private static string ComputeDateMonthsBefore(string anchor, int months)
    {
        if (!DateTime.TryParse(anchor, out var dt))
            throw new ArgumentException("Invalid anchor date");
        var back = dt.AddMonths(-months);
        return new DateTime(back.Year, back.Month, 1).ToString("yyyy-MM-dd");
    }
}
