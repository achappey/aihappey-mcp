using System.ComponentModel;
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
    [McpServerTool(OpenWorld = false, ReadOnly = true)]
    [Description("Returns a list of payments received.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetPayments(
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Payment date (YYY-MM-DD)")] string paymentDate,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithStructuredContent(async () =>
    {
        if (string.IsNullOrWhiteSpace(paymentDate)) throw new ArgumentException(null, nameof(paymentDate));

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/invoices/payment");
        var filters = new List<string>
        {
            $"q[date]={Uri.EscapeDataString(paymentDate)}"
        };

        var filterString = string.Join("&", filters);
        var payments = await downloadService.GetAllSimplicatePagesAsync<SimplicatePayment>(
            serviceProvider,
            requestContext.Server,
            baseUrl,
            filterString,
            pageNum => $"Downloading payments",
            requestContext,
            cancellationToken: cancellationToken
        );

        List<SimplicateInvoicePayment> invoicePayments = [];

        foreach (var payment in payments)
        {
            string paymentsBaseUrl = simplicateOptions.GetApiUrl($"/invoices/invoice/{payment.InvoiceId}");
            string paymentSelect = "invoice_number";
            var paymentFilterString = $"?select={paymentSelect}";
            var fullUrl = $"{paymentsBaseUrl}{paymentFilterString}";

            var invoice = await downloadService.GetSimplicateItemAsync<SimplicateInvoice>(serviceProvider, requestContext.Server, fullUrl, cancellationToken);
            invoicePayments.Add(new SimplicateInvoicePayment()
            {
                InvoiceNumber = invoice?.Data?.InvoiceNumber!,
                Date = payment.Date,
                Amount = payment.Amount,
                InvoiceId = payment.InvoiceId,
            });
        }

        return invoicePayments;
    });


    [McpServerTool(OpenWorld = false, ReadOnly = true, Destructive = false)]
    [Description("Returns, per my organization profile, a summary of paid invoices: average, minimum, and maximum payment term (days between invoice date and payment date), optionally filtered by date range and organization.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetAveragePaymentTermByMyOrganization(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,
      string? fromDate = null,
      string? toDate = null,
      string? organizationName = null,
      CancellationToken cancellationToken = default) =>
      await requestContext.WithStructuredContent(async () =>
    {
        if (string.IsNullOrWhiteSpace(fromDate) && string.IsNullOrWhiteSpace(toDate) && string.IsNullOrWhiteSpace(organizationName))
            throw new ArgumentException("At least one filter (fromDate, toDate, organizationName, invoiceStatus) must be provided.");


        if (string.IsNullOrWhiteSpace(organizationName) && !string.IsNullOrWhiteSpace(fromDate) && !string.IsNullOrWhiteSpace(toDate))
        {
            if (DateTime.TryParse(fromDate, out var fromDt) && DateTime.TryParse(toDate, out var toDt))
            {
                if ((toDt - fromDt).TotalDays > 65)
                    throw new ArgumentException("The date range cannot exceed 65 days.");
            }
        }

        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        string baseUrl = simplicateOptions.GetApiUrl("/invoices/invoice");
        string select = "total_including_vat,total_excluding_vat,total_outstanding,my_organization_profile.,date,id";
        var filters = new List<string> { "q[status.label]=Payed" };

        if (!string.IsNullOrWhiteSpace(fromDate)) filters.Add($"q[date][ge]={Uri.EscapeDataString(fromDate)}");
        if (!string.IsNullOrWhiteSpace(toDate)) filters.Add($"q[date][le]={Uri.EscapeDataString(toDate)}");
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

        var invoiceDetails = new List<(string Org, PaidInvoicePaymentTermDetail Detail)>();
        foreach (var invoice in invoices)
        {
            if (!DateTime.TryParse(invoice.Date, out var invoiceDate))
                continue;

            string paymentsBaseUrl = simplicateOptions.GetApiUrl("/invoices/payment");

            string paymentSelect = "date,amount,invoice_id";
            var paymentFilters = new List<string> { $"q[invoice_id]={invoice.Id}" };
            var paymentFilterString = string.Join("&", paymentFilters) + $"&select={paymentSelect}";

            var invoicePayments = await downloadService.GetAllSimplicatePagesAsync<SimplicatePayment>(
                serviceProvider,
                requestContext.Server,
                paymentsBaseUrl,
                paymentFilterString,
                pageNum => $"Downloading payments {invoice.Id}",
                requestContext,
                cancellationToken: cancellationToken
            );

            if (invoicePayments.Count == 0)
                continue;

            var totalPaid = invoicePayments.Sum(p => p.Amount);
            var lastPaymentDate = invoicePayments
                .Select(p => p.Date.ParseDate())
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (lastPaymentDate == default)
                continue;

            invoiceDetails.Add((
                invoice.MyOrganizationProfile?.Organization?.Name ?? string.Empty,
                new PaidInvoicePaymentTermDetail
                {
                    Id = invoice.Id,
                    Date = invoice.Date,
                    PaymentDate = lastPaymentDate,
                    PaymentTermDays = (lastPaymentDate - invoiceDate).Days,
                    AmountPaid = totalPaid
                }
            ));
        }

        return invoiceDetails
               .GroupBy(x => x.Org)
               .ToDictionary(
                   g => g.Key,
                   g =>
                   {
                       var list = g.Select(x => x.Detail).ToList();
                       return new PaidInvoicePaymentTermSummary
                       {
                           TotalInvoices = list.Count,
                           AveragePaymentTermDays = list.Count != 0 ? list.Average(x => x.PaymentTermDays) : 0,
                           MinPaymentTermDays = list.Count != 0 ? list.Min(x => x.PaymentTermDays) : null,
                           MaxPaymentTermDays = list.Count != 0 ? list.Max(x => x.PaymentTermDays) : null,
                       };
                   }
               );
    });
}

