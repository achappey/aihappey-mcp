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

    [Description("Get Simplicate sales with optional filters.")]
    [McpServerTool(Title = "Get Simplicate sales",
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(SimplicateData<SimplicateSalesItem>),
        ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateSales_GetSales(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Status label filter (e.g. open/scored/missed). Optional.")] string? statusLabel = null,
        [Description("Progress (stage) label filter. Optional.")] string? progressLabel = null,
        [Description("Responsible employee name (contains). Optional.")] string? responsibleEmployeeName = null,
        [Description("Organization name (contains). Optional.")] string? organisationName = null,
        [Description("Person full name (exact). Optional.")] string? personName = null,
        [Description("Team name (contains). Optional.")] string? teamName = null,
        [Description("Source name (exact). Optional.")] string? sourceName = null,
        [Description("The limit of max allowed results.")] int? limit = null,
        [Description("The offset to search from.")] int? offset = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async ()
        => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/sales/sales");
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(statusLabel)) filters.Add($"q[status.label]={Uri.EscapeDataString(statusLabel)}");
        if (!string.IsNullOrWhiteSpace(progressLabel)) filters.Add($"q[progress.label]={Uri.EscapeDataString(progressLabel)}");
        if (!string.IsNullOrWhiteSpace(responsibleEmployeeName)) filters.Add($"q[responsible_employee.name]=*{Uri.EscapeDataString(responsibleEmployeeName)}*");
        if (!string.IsNullOrWhiteSpace(organisationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organisationName)}*");
        if (!string.IsNullOrWhiteSpace(personName)) filters.Add($"q[person.full_name]={Uri.EscapeDataString(personName)}");
        if (!string.IsNullOrWhiteSpace(teamName)) filters.Add($"q[teams.name]=*{Uri.EscapeDataString(teamName)}*");
        if (!string.IsNullOrWhiteSpace(sourceName)) filters.Add($"q[source.name]={Uri.EscapeDataString(sourceName)}");
        if (limit.HasValue) filters.Add($"limit={limit}");
        if (offset.HasValue) filters.Add($"offset={offset}");

        string filterString = string.Join("&", filters);

        if (limit.HasValue && limit.Value <= 100)
            return await downloadService.GetSimplicatePageAsync<SimplicateSalesItem>(
                serviceProvider,
                requestContext.Server,
                $"{baseUrl}?{filterString}&metadata=count",
                cancellationToken: cancellationToken
            );

        var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateSalesItem>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading sales (page {pageNum})",
            requestContext,
            cancellationToken: cancellationToken
        );

        return new SimplicateData<SimplicateSalesItem>()
        {
            Data = items.Skip(offset ?? 0).Take(limit ?? int.MaxValue),
            Metadata = new()
            {
                Count = items.Count,
                Offset = offset ?? null,
                Limit = limit ?? null
            }

        };
    }));

    [Description("Get Simplicate sales quotes with optional filters.")]
    [McpServerTool(Title = "Get Simplicate sales quotes",
    OpenWorld = false,
    UseStructuredContent = true,
    OutputSchemaType = typeof(SimplicateData<SimplicateQuoteItem>),
    ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateSales_GetSalesQuotes(
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("The limit of max allowed results.")] int? limit = null,
    [Description("The offset to search from.")] int? offset = null,
    CancellationToken cancellationToken = default)
    => await ModelContextToolExtensions.WithExceptionCheck(async ()
    => await requestContext.WithStructuredContent(async () =>
{
    var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
    var downloadService = serviceProvider.GetRequiredService<DownloadService>();

    string baseUrl = simplicateOptions.GetApiUrl("/sales/quote");
    var filters = new List<string>();
    if (limit.HasValue) filters.Add($"limit={limit}");
    if (offset.HasValue) filters.Add($"offset={offset}");
    string filterString = string.Join("&", filters);

    if (limit.HasValue && limit.Value <= 100)
        return await downloadService.GetSimplicatePageAsync<SimplicateQuoteItem>(
            serviceProvider,
            requestContext.Server,
            $"{baseUrl}?{filterString}&metadata=count",
            cancellationToken: cancellationToken
        );

    var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateQuoteItem>(
        serviceProvider,
        requestContext.Server,
        baseUrl,
        filterString,
        pageNum => $"Downloading sales quotes (page {pageNum})",
        requestContext,
        cancellationToken: cancellationToken
    );

    return new SimplicateData<SimplicateQuoteItem>()
    {
        Data = items.Skip(offset ?? 0).Take(limit ?? int.MaxValue),
        Metadata = new()
        {
            Count = items.Count,
            Offset = offset ?? null,
            Limit = limit ?? null
        }
    };
}));
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

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = [];
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