using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Invoices.Models;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Invoices;

public static partial class SimplicateInvoices
{
    [McpServerTool(OpenWorld = false,
        Destructive = false,
        Name = "simplicate_invoices_get_with_days_open",
        Title = "Get open invoices by customer",
        ReadOnly = true)]
    [Description("Returns, per own organization profile, a grouped summary of outstanding debtors: for each customer, shows the total outstanding amount, number of open invoices, and the average number of days invoices have been open (as of today). Perfect for actionable debtor KPI dashboards without hardcoded periods.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetWithDaysOpenByMyOrganization(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Optional organization name to filter on")] string? organizationName = null,
            CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        string baseUrl = simplicateOptions.GetApiUrl("/invoices/invoice");

        var filters = new List<string>
        {
            "q[status.label][in]=Sended,Expired"
        };

        if (!string.IsNullOrWhiteSpace(organizationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organizationName)}*");

        var filterString = string.Join("&", filters);
        var invoices = await downloadService.GetAllSimplicatePagesAsync<SimplicateInvoice>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading open invoices",
            requestContext,
            cancellationToken: cancellationToken
        );

        return new
        {
            my_organization_profiles = invoices
                .GroupBy(x => x.MyOrganizationProfile?.Organization?.Name ?? string.Empty)
                .Select(a => new
                {
                    name = a.Key,
                    debtors = a.GroupBy(x => x.Organization?.Name ?? string.Empty)
                        .Select(cg =>
                        {
                            return new SimplicateOpenInvoiceWithDaysOpen
                            {
                                DebtorName = cg.Key,
                                TotalInvoices = cg.Count(),
                                TotalOutstanding = cg.Sum(x => x.TotalOutstanding),
                                TotalIncludingVat = cg.Sum(x => x.TotalIncludingVat),
                                TotalExcludingVat = cg.Sum(x => x.TotalExcludingVat),
                                AverageDaysOpen = (int?)cg.Average(x => x.DaysOpen),
                            };
                        })
                        .OrderByDescending(x => x.TotalOutstanding)
                        .ToList()
                })
        };
    });

    [McpServerTool(OpenWorld = false,
        Destructive = false,
        Name = "simplicate_invoices_get_expired_by_project_manager",
        Title = "Get expired invoices by project manager",
        ReadOnly = true)]
    [Description("Returns, per project manager, a list of invoices with invoice number and amount. Ideal for project control and cashflow management.")]
    public static async Task<CallToolResult?>
        SimplicateInvoices_GetExpiredByProjectManager(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Optional invoice status to filter on")] InvoiceStatusLabel invoiceStatus = InvoiceStatusLabel.Expired,
            [Description("Optional organization name to filter on")] string? organizationName = null,
            [Description("Optional project manager name to filter on")] string? projectManagerName = null,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/invoices/invoice");
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(organizationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organizationName)}*");
        filters.Add($"q[status.label]={invoiceStatus.GetEnumMemberValue()}");

        var filterString = string.Join("&", filters);
        var invoices = await downloadService.GetAllSimplicatePagesAsync<SimplicateInvoice>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading invoices",
            requestContext,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        var now = DateTime.UtcNow;

        return new
        {
            project_managers = invoices
                .Where(x => x.Projects?.FirstOrDefault()?.ProjectManager?.Name != null)
                .GroupBy(x => x.Projects?.FirstOrDefault()?.ProjectManager?.Name!)
                .Where(a => string.IsNullOrEmpty(projectManagerName)
                    || a.Key.Contains(projectManagerName, StringComparison.OrdinalIgnoreCase))
                    .Select(a => new
                    {
                        project_manager = a.Key,
                        invoices = a.OrderBy(z => z.Date.ParseDate()).ToList()
                    })

        };
    });

    [McpServerTool(OpenWorld = false, ReadOnly = true,
        Destructive = false,
        Name = "simplicate_invoices_get_totals_by_my_organization",
        Title = "Get invoice totals by my organization")]
    [Description("Get total invoices grouped by my organization profile, optionally filtered by date range and organization.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetTotalsByMyOrganization(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fromDate = null,
        string? toDate = null,
        string? organizationName = null,
        InvoiceStatusLabel? invoiceStatus = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithStructuredContent(async () =>
    {
        if (string.IsNullOrWhiteSpace(fromDate) && string.IsNullOrWhiteSpace(toDate) && string.IsNullOrWhiteSpace(organizationName)
            && !invoiceStatus.HasValue)
            throw new ArgumentException("At least one filter (fromDate, toDate, organizationName, invoiceStatus) must be provided.");

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/invoices/invoice");
        string select = "total_including_vat,total_excluding_vat,total_outstanding,my_organization_profile.,payment_term.,date";
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(fromDate)) filters.Add($"q[date][ge]={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate)) filters.Add($"q[date][le]={Uri.EscapeDataString(toDate)}");
        if (invoiceStatus.HasValue) filters.Add($"q[status.label]={invoiceStatus.Value}");
        if (!string.IsNullOrWhiteSpace(organizationName)) filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organizationName)}*");

        var filterString = string.Join("&", filters) + $"&select={select}";

        var invoices = await downloadService.GetAllSimplicatePagesAsync<SimplicateInvoice>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading invoices",
            requestContext,
            cancellationToken: cancellationToken
        );

        return new
        {
            my_organization_profiles = invoices
                .GroupBy(x => x.MyOrganizationProfile?.Organization?.Name ?? string.Empty)
                .Select(a => new
                {
                    name = a.Key,
                    invoices = a
                    .GroupBy(x =>
                        {
                            if (DateTime.TryParse(x.Date, out var date))
                                return date.ToString("yyyy-MM");
                            return "Unknown";
                        })
                            .Select(cg =>
                            {
                                return new SimplicateTotalInvoices
                                {
                                    Month = cg.Key,
                                    TotalInvoices = cg.Count(),
                                    TotalIncludingVat = cg.Sum(x => x.TotalIncludingVat),
                                    TotalExcludingVat = cg.Sum(x => x.TotalExcludingVat)
                                };
                            })
                            .OrderBy(x => x.Month)
                            .ToList(),
                })
        };
    });
}

