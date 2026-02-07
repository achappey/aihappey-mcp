using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{

    [Description("Please fill in the sales details")]
    public class SimplicateNewSales
    {
        [JsonPropertyName("subject")]
        [Required]
        [Description("The sales subject.")]
        public string? Subject { get; set; }

        [JsonPropertyName("note")]
        [Description("A note or description about the organization.")]
        public string? Note { get; set; }

    }

    [Description("Create a new sales in Simplicate Sales")]
    [McpServerTool(Title = "Create new sales in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateSales_CreateSales(
        [Description("The sales subject.")] string subject,
        [Description("Organization id.")] string organizationId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("A note or description about the sales.")] string? note = null,
        CancellationToken cancellationToken = default) 
        => await serviceProvider.PostSimplicateResourceAsync(
                requestContext,
                "/sales/sales",
               new SimplicateNewSales
               {
                   Subject = subject,
                   Note = note,
               },
                dto => new
                {
                    subject = dto.Subject,
                    note = dto.Note,
                    organization_id = organizationId,
                },
                cancellationToken
            );

    [Description("Update an sales in Simplicate Sales")]
    [McpServerTool(Title = "Update sales in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateSales_UpdateSales(
            string salesId,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            string? subject = null,
            string? note = null,
            CancellationToken cancellationToken = default)
    {
        var dto = new SimplicateNewSales
        {
            Subject = subject,
            Note = note,
        };

        return await serviceProvider.PutSimplicateResourceMergedAsync(
            requestContext,
            "/sales/sales/" + salesId,
            dto,
            d => new
            {
                name = d.Subject,
                note = d.Note
            },
            cancellationToken);
    }

    // === NIEUWE TOOL: Sales totalen per opdrachtgever ===
    [Description("Get total sales grouped by organization (opdrachtgever) with optional filters and measure (count or amount).")]
    [McpServerTool(Title = "Get Simplicate sales totals by organization",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateSales_GetTotalsByOrganization(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Date field to filter on. Default: ExpectedClosingDate.")] SalesDateField dateField = SalesDateField.ExpectedClosingDate,
        [Description("Start date (inclusive), yyyy-MM-dd. Optional.")] string? fromDate = null,
        [Description("End date (inclusive), yyyy-MM-dd. Optional.")] string? toDate = null,
        [Description("Measure: Count or Amount (Amount uses expected_revenue).")] Measure measure = Measure.Count,
        [Description("Status label filter (e.g. open/scored/missed). Optional.")] string? statusLabel = null,
        [Description("Progress (stage) label filter (e.g. Lead/Offerte). Optional.")] string? progressLabel = null,
        [Description("Responsible employee name (contains). Optional.")] string? responsibleEmployeeName = null,
        [Description("Organization name (contains). Optional.")] string? organisationName = null,
        [Description("Person full name (exact). Optional.")] string? personName = null,
        [Description("Team name (contains). Optional.")] string? teamName = null,
        [Description("Source name (exact). Optional.")] string? sourceName = null,
        [Description("Probability >= (0..100). Optional.")] decimal? probabilityGe = null,
        [Description("Probability <= (0..100). Optional.")] decimal? probabilityLe = null,
        [Description("Expected revenue >=. Optional.")] decimal? amountGe = null,
        [Description("Expected revenue <=. Optional.")] decimal? amountLe = null,
        [Description("Include rows for unknown organization (no organization on the sale). Default: false.")] bool includeUnknownOrganization = false,
        [Description("Sort by Count or Amount. Default: Amount.")] SortBy sortBy = SortBy.Amount,
        [Description("Sort descending. Default: true.")] bool descending = true,
        [Description("Return only the top N organizations after sorting. Optional.")] int? top = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/sales/sales");
        var filters = new List<string>();

        string dateFieldQuery = dateField switch
        {
            SalesDateField.CreatedAt => "created_at",
            SalesDateField.UpdatedAt => "updated_at",
            SalesDateField.StartDate => "start_date",
            _ => "expected_closing_date"
        };

        if (!string.IsNullOrWhiteSpace(fromDate)) filters.Add($"q[{dateFieldQuery}][ge]={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate)) filters.Add($"q[{dateFieldQuery}][le]={Uri.EscapeDataString(toDate)}");

        if (!string.IsNullOrWhiteSpace(statusLabel)) filters.Add($"q[status.label]={Uri.EscapeDataString(statusLabel)}");
        if (!string.IsNullOrWhiteSpace(progressLabel)) filters.Add($"q[progress.label]={Uri.EscapeDataString(progressLabel)}");
        if (!string.IsNullOrWhiteSpace(responsibleEmployeeName)) filters.Add($"q[responsible_employee.name]=*{Uri.EscapeDataString(responsibleEmployeeName)}*");
        if (!string.IsNullOrWhiteSpace(organisationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organisationName)}*");
        if (!string.IsNullOrWhiteSpace(personName)) filters.Add($"q[person.full_name]={Uri.EscapeDataString(personName)}");
        if (!string.IsNullOrWhiteSpace(teamName)) filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");
        if (!string.IsNullOrWhiteSpace(sourceName)) filters.Add($"q[source.name]={Uri.EscapeDataString(sourceName)}");
        if (probabilityGe.HasValue) filters.Add($"q[chance_to_score][ge]={probabilityGe.Value}");
        if (probabilityLe.HasValue) filters.Add($"q[chance_to_score][le]={probabilityLe.Value}");
        if (amountGe.HasValue) filters.Add($"q[expected_revenue][ge]={amountGe.Value}");
        if (amountLe.HasValue) filters.Add($"q[expected_revenue][le]={amountLe.Value}");

        // Minimal select — zorg dat organization velden mee komen
        filters.Add("select=expected_revenue,chance_to_score,status.,progress.,organization.,responsible_employee.,teams.,source.,created_at,updated_at,start_date,expected_closing_date");

        string filterString = string.Join("&", filters);

        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateSalesItem>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading sales (page {pageNum})",
            requestContext,
            cancellationToken: cancellationToken
        );

        // Groeperen per opdrachtgever
        var grouped = items
            .Where(x => includeUnknownOrganization || x.organization != null)
            .GroupBy(x => new OrganizationKey
            {
                id = x.organization?.id ?? "unknown",
                name = x.organization?.name ?? "(onbekend)",
                relation_number = x.organization?.relation_number
            })
            .Select(g => new OrganizationTotals
            {
                Organization = g.Key,
                Count = measure == Measure.Count ? g.Count() : 0,
                Amount = measure == Measure.Amount ? g.Sum(r => r.expected_revenue ?? 0m) : 0m
            });

        // Sorteren
        IOrderedEnumerable<OrganizationTotals> ordered = sortBy == SortBy.Count
            ? (descending ? grouped.OrderByDescending(x => x.Count) : grouped.OrderBy(x => x.Count))
            : (descending ? grouped.OrderByDescending(x => x.Amount) : grouped.OrderBy(x => x.Amount));

        var result = (top.HasValue && top.Value > 0) ? ordered.Take(top.Value).ToList() : [.. ordered];

        // Terug als JSON content block (zoals de andere tools)
        return new
        {
            organizationTotals = result
        };
    }));

    // === Extra helper types voor deze tool ===
    public enum SortBy { Count, Amount }

    public sealed class OrganizationKey
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? relation_number { get; set; }
    }

    public sealed class OrganizationTotals
    {
        public OrganizationKey Organization { get; set; } = new OrganizationKey();
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }

    // === TOOLS ===
    [Description("Get total sales per month within a date range with optional filters and measure (count or amount).")]
    [McpServerTool(Title = "Get Simplicate sales totals by month",
        Name = "simplicate_sales_get_totals_by_month",
        OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateSales_GetTotalsByMonth(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Date field to aggregate on. Default: ExpectedClosingDate.")] SalesDateField dateField = SalesDateField.ExpectedClosingDate,
        [Description("Start date (inclusive), yyyy-MM-dd. Optional.")] string? fromDate = null,
        [Description("End date (inclusive), yyyy-MM-dd. Optional.")] string? toDate = null,
        [Description("Override amount field (optional). Default for sales is expected_revenue.")] string? amountField = null,
        [Description("Status label filter (e.g. open/scored/missed). Optional.")] string? statusLabel = null,
        [Description("Progress (stage) label filter. Optional.")] string? progressLabel = null,
        [Description("Responsible employee name (contains). Optional.")] string? responsibleEmployeeName = null,
        [Description("Organization name (contains). Optional.")] string? organisationName = null,
        [Description("Person full name (exact). Optional.")] string? personName = null,
        [Description("Team name (contains). Optional.")] string? teamName = null,
        [Description("Source name (exact). Optional.")] string? sourceName = null,
        [Description("Probability >= (0..100). Optional.")] decimal? probabilityGe = null,
        [Description("Probability <= (0..100). Optional.")] decimal? probabilityLe = null,
        [Description("Expected revenue >=. Optional.")] decimal? amountGe = null,
        [Description("Expected revenue <=. Optional.")] decimal? amountLe = null,
        [Description("Custom field (fieldname) filter. Optional.")] string? customFieldName = null,
        [Description("Custom field (value) filter. Optional.")] string? customFieldValue = null,
        [Description("Include months without data. Default: true.")] bool includeZeroMonths = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/sales/sales");
        var filters = new List<string>();

        string dateFieldQuery = dateField switch
        {
            SalesDateField.CreatedAt => "created_at",
            SalesDateField.UpdatedAt => "updated_at",
            SalesDateField.StartDate => "start_date",
            _ => "expected_closing_date"
        };

        if (!string.IsNullOrWhiteSpace(fromDate)) filters.Add($"q[{dateFieldQuery}][ge]={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate)) filters.Add($"q[{dateFieldQuery}][le]={Uri.EscapeDataString(toDate)}");

        if (!string.IsNullOrWhiteSpace(statusLabel)) filters.Add($"q[status.label]={Uri.EscapeDataString(statusLabel)}");
        if (!string.IsNullOrWhiteSpace(progressLabel)) filters.Add($"q[progress.label]={Uri.EscapeDataString(progressLabel)}");
        if (!string.IsNullOrWhiteSpace(responsibleEmployeeName)) filters.Add($"q[responsible_employee.name]=*{Uri.EscapeDataString(responsibleEmployeeName)}*");
        if (!string.IsNullOrWhiteSpace(organisationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organisationName)}*");
        if (!string.IsNullOrWhiteSpace(personName)) filters.Add($"q[person.full_name]={Uri.EscapeDataString(personName)}");
        if (!string.IsNullOrWhiteSpace(teamName)) filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");
        if (!string.IsNullOrWhiteSpace(sourceName)) filters.Add($"q[source.name]={Uri.EscapeDataString(sourceName)}");

        if (!string.IsNullOrWhiteSpace(customFieldName)
            && !string.IsNullOrWhiteSpace(customFieldValue)) filters.Add($"q[custom_fields.{Uri.EscapeDataString(customFieldName)}]={Uri.EscapeDataString(customFieldValue)}");

        if (probabilityGe.HasValue) filters.Add($"q[chance_to_score][ge]={probabilityGe.Value}");
        if (probabilityLe.HasValue) filters.Add($"q[chance_to_score][le]={probabilityLe.Value}");
        if (amountGe.HasValue) filters.Add($"q[expected_revenue][ge]={amountGe.Value}");
        if (amountLe.HasValue) filters.Add($"q[expected_revenue][le]={amountLe.Value}");

        // Minimal select to reduce payload
        filters.Add("select=expected_revenue,chance_to_score,status.,progress.,organization.,responsible_employee.,teams.,source.,created_at,updated_at,start_date,expected_closing_date");

        string filterString = string.Join("&", filters);

        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateSalesItem>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading sales (page {pageNum})",
            requestContext,
            cancellationToken: cancellationToken
        );

        Func<SimplicateSalesItem, DateTime?> dateSelector = dateField switch
        {
            SalesDateField.CreatedAt => x => x.created_at,
            SalesDateField.UpdatedAt => x => x.updated_at,
            SalesDateField.StartDate => x => x.start_date,
            _ => x => x.expected_closing_date
        };

        string usedAmountField = string.IsNullOrWhiteSpace(amountField) ? "expected_revenue" : amountField;

        var grouped = items
            .Select(x => new
            {
                Period = TruncateToMonth(dateSelector(x)),
                Count = 1,
                Amount = GetSalesAmount(x, usedAmountField)
            })
            .Where(x => x.Period.HasValue)
            .GroupBy(x => x.Period!.Value)
            .ToDictionary(
                g => g.Key,
                g => new MonthlyTotals
                {
                    Count = g.Sum(r => r.Count),
                    Amount = g.Sum(r => r.Amount)
                }
            );

        var completed = includeZeroMonths
            ? EnsureZeroMonths(grouped, fromDate, toDate)
            : grouped;

        return new
        {
            Data = completed.OrderBy(kv => kv.Key)
                  .Select(kv => new
                  {
                      Month = kv.Key.ToString("yyyy-MM-01"),
                      Totals = kv.Value
                  })
        };

        // Sort by month for predictable output
        /*  var ordered = completed.OrderBy(kv => kv.Key)
              .ToDictionary(kv => kv.Key.ToString("yyyy-MM-01"), kv => kv.Value);

          return ordered.ToJsonContentBlock($"{baseUrl}?{filterString}").ToCallToolResult();*/
    });

    [Description("Get total quotes per month within a date range with optional filters and measure (count or amount).")]
    [McpServerTool(Title = "Get Simplicate quote totals by month", Name = "simplicate_sales_get_quote_totals_by_month",
        OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateSales_GetQuoteTotalsByMonth(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Date field to aggregate on. Default: QuoteDate.")] QuotesDateField dateField = QuotesDateField.QuoteDate,
        [Description("Start date (inclusive), yyyy-MM-dd. Optional.")] string? fromDate = null,
        [Description("End date (inclusive), yyyy-MM-dd. Optional.")] string? toDate = null,
        [Description("Override amount field (optional). Default for quotes is total_excl_vat.")] string? amountField = null,
        [Description("Quote status label filter. Optional.")] string? quotationStatusLabel = null,
        [Description("Responsible employee name (contains). Optional.")] string? responsibleEmployeeName = null,
        [Description("Organization name (exact or contains). Optional.")] string? organisationName = null,
        [Description("Person full name (exact). Optional.")] string? personName = null,
        [Description("Quote template label as proposition. Optional.")] string? propositionLabel = null,
        [Description("Amount >= (excl. VAT). Optional.")] decimal? amountGe = null,
        [Description("Amount <= (excl. VAT). Optional.")] decimal? amountLe = null,
        [Description("Include months without data. Default: true.")] bool includeZeroMonths = true,
        CancellationToken cancellationToken = default)
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/sales/quote");
        var filters = new List<string>();

        string dateFieldQuery = dateField switch
        {
            QuotesDateField.CreatedAt => "created_at",
            QuotesDateField.UpdatedAt => "updated_at",
            _ => "quote_date" // Simplicate uses 'date' for quotes; keep quote_date property mapped
        };

        if (!string.IsNullOrWhiteSpace(fromDate)) filters.Add($"q[{dateFieldQuery}][ge]={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate)) filters.Add($"q[{dateFieldQuery}][le]={Uri.EscapeDataString(toDate)}");

        if (!string.IsNullOrWhiteSpace(quotationStatusLabel)) filters.Add($"q[quotestatus.label]={Uri.EscapeDataString(quotationStatusLabel)}");
        if (!string.IsNullOrWhiteSpace(responsibleEmployeeName)) filters.Add($"q[responsible_employee.name]=*{Uri.EscapeDataString(responsibleEmployeeName)}*");
        if (!string.IsNullOrWhiteSpace(organisationName)) filters.Add($"q[organization.name]={Uri.EscapeDataString(organisationName)}");
        if (!string.IsNullOrWhiteSpace(personName)) filters.Add($"q[person.full_name]={Uri.EscapeDataString(personName)}");
        if (!string.IsNullOrWhiteSpace(propositionLabel)) filters.Add($"q[quotetemplate.label]={Uri.EscapeDataString(propositionLabel)}");
        if (amountGe.HasValue) filters.Add($"q[total_excl_vat][ge]={amountGe.Value}");
        if (amountLe.HasValue) filters.Add($"q[total_excl_vat][le]={amountLe.Value}");

        // Minimal select to reduce payload
        filters.Add("select=quote_date,date,quotestatus.,total_excl_vat,quotetemplate.,organization.,responsible_employee.,created_at,updated_at");

        string filterString = string.Join("&", filters);

        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateQuoteItem>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading quotes (page {pageNum})",
            requestContext,
            cancellationToken: cancellationToken
        );

        Func<SimplicateQuoteItem, DateTime?> dateSelector = dateField switch
        {
            QuotesDateField.CreatedAt => x => x.created_at,
            QuotesDateField.UpdatedAt => x => x.updated_at,
            _ => x => x.quote_date ?? x.created_at // fall back if needed
        };

        string usedAmountField = string.IsNullOrWhiteSpace(amountField) ? "total_excl_vat" : amountField;

        var grouped = items
            .Select(x => new
            {
                Period = TruncateToMonth(dateSelector(x)),
                Count = 1,
                Amount = GetQuoteAmount(x, usedAmountField)
            })
            .Where(x => x.Period.HasValue)
            .GroupBy(x => x.Period!.Value)
            .ToDictionary(
                g => g.Key,
                g => new MonthlyTotals
                {
                    Count = g.Sum(r => r.Count),
                    Amount = g.Sum(r => r.Amount)
                }
            );

        var completed = includeZeroMonths
            ? EnsureZeroMonths(grouped, fromDate, toDate)
            : grouped;

        return new
        {
            Data = completed.OrderBy(kv => kv.Key)
                    .Select(kv => new
                    {
                        Month = kv.Key.ToString("yyyy-MM-01"),
                        Totals = kv.Value
                    })
        };
    });

    // === Helpers ===
    private static DateTime? TruncateToMonth(DateTime? dt)
    {
        if (!dt.HasValue) return null;
        var d = dt.Value;
        return new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static decimal GetSalesAmount(SimplicateSalesItem x, string field)
    {
        return field switch
        {
            "expected_revenue" => x.expected_revenue ?? 0m,
            _ => 0m
        };
    }

    private static decimal GetQuoteAmount(SimplicateQuoteItem x, string field)
    {
        return field switch
        {
            "total_excl" => x.total_excl ?? 0m,
            _ => 0m
        };
    }

    private static IDictionary<DateTime, MonthlyTotals> EnsureZeroMonths(
        IDictionary<DateTime, MonthlyTotals> data,
        string? fromDate,
        string? toDate)
    {
        if (!DateTime.TryParse(fromDate, out var fromDt) || !DateTime.TryParse(toDate, out var toDt))
            return data;

        var cursor = new DateTime(fromDt.Year, fromDt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(toDt.Year, toDt.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = new Dictionary<DateTime, MonthlyTotals>(data);
        while (cursor <= end)
        {
            if (!result.ContainsKey(cursor))
                result[cursor] = new MonthlyTotals { Count = 0, Amount = 0m };
            cursor = cursor.AddMonths(1);
        }
        return result;
    }
}


// === Enums ===
public enum Measure { Count, Amount }

public enum SalesDateField
{
    CreatedAt,
    UpdatedAt,
    ExpectedClosingDate,
    StartDate
}

public enum QuotesDateField
{
    QuoteDate,   // maps to date or quote_date depending on your API
    CreatedAt,
    UpdatedAt
}

// === DTOs (minimal) ===
public sealed class SimplicateSalesItem
{
    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? created_at { get; set; }

    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? updated_at { get; set; }

    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? expected_closing_date { get; set; }

    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? start_date { get; set; }

    public SalesStatus? status { get; set; }
    public SalesProgress? progress { get; set; }
    public decimal? expected_revenue { get; set; }
    public Organization? organization { get; set; }
    public Employee? responsible_employee { get; set; }
    public Team[]? teams { get; set; }
    public Source? source { get; set; }
    public decimal? chance_to_score { get; set; }
}

public sealed class SimplicateQuoteItem
{
    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? quote_date { get; set; }     // aka date

    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? created_at { get; set; }

    [JsonConverter(typeof(SimplicateDateTimeConverter))]
    public DateTime? updated_at { get; set; }
    public QuoteStatus? quotestatus { get; set; }
    public decimal? total_excl { get; set; }
    public QuoteTemplate? quotetemplate { get; set; }
    public Organization? organization { get; set; }
    public Employee? responsible_employee { get; set; }
}

public sealed class SalesStatus { public string? label { get; set; } }
public sealed class SalesProgress { public string? label { get; set; } }
public sealed class QuoteStatus { public string? label { get; set; } }
public sealed class QuoteTemplate { public string? label { get; set; } }
public sealed class Organization
{
    public string? id { get; set; }
    public string? name { get; set; }
    public string? relation_number { get; set; }
}
public sealed class Employee { public string? name { get; set; } }
public sealed class Team { public string? name { get; set; } }
public sealed class Source { public string? name { get; set; } }

// === Output type ===
public sealed class MonthlyTotals
{
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public class SimplicateDateTimeConverter : JsonConverter<DateTime?>
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str))
            return null;

        if (DateTime.TryParseExact(str,
                                   Format,
                                   CultureInfo.InvariantCulture,
                                   DateTimeStyles.AssumeLocal,
                                   out var dt))
        {
            return dt;
        }

        // fallback: try normal parsing
        if (DateTime.TryParse(str, out dt))
            return dt;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString(Format));
        else
            writer.WriteNullValue();
    }
}