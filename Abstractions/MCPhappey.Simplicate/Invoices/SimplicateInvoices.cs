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
    [McpServerTool(OpenWorld = false,
       Destructive = false,
       UseStructuredContent = true,
       OutputSchemaType = typeof(SimplicateData<SimplicateInvoice>),
       Name = "simplicate_invoices_get_invoices",
       Title = "Get Simplicate invoices",
       ReadOnly = true)]
    [Description("Get Simplicate invoices with optional filters.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetInvoices(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Optional organization name filter")] string? organizationName = null,
           [Description("Optional status label filter")] string? statusLabel = null,
           CancellationToken cancellationToken = default)
           => await requestContext.WithStructuredContent(async () =>
   {
       var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
       var downloadService = serviceProvider.GetRequiredService<DownloadService>();
       string baseUrl = simplicateOptions.GetApiUrl("/invoices/invoice");

       var filters = new List<string>
       {
       };

       if (!string.IsNullOrWhiteSpace(organizationName))
           filters.Add($"q[organization.name]=*{Uri.EscapeDataString(organizationName)}*");

       if (!string.IsNullOrWhiteSpace(statusLabel))
           filters.Add($"q[status.label]={Uri.EscapeDataString(statusLabel)}");

       var filterString = string.Join("&", filters);
       var invoices = await downloadService.GetAllSimplicatePagesAsync<SimplicateInvoice>(
           serviceProvider,
           requestContext.Server,
           baseUrl,
           filterString,
           pageNum => $"Downloading invoices",
           requestContext,
           cancellationToken: cancellationToken
       );

       return new SimplicateData<SimplicateInvoice>()
       {
           Data = invoices
       };
   });
}

