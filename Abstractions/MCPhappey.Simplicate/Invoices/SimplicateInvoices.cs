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
           [Description("The limit of max allowed results.")] int? limit = null,
           [Description("The offset to search from.")] int? offset = null,
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

       if (limit.HasValue) filters.Add($"limit={limit}");
       if (offset.HasValue) filters.Add($"offset={offset}");

       var filterString = string.Join("&", filters);

       if (limit.HasValue && limit.Value <= 100)
           return await downloadService.GetSimplicatePageAsync<SimplicateInvoice>(
               serviceProvider,
               requestContext.Server,
               $"{baseUrl}?{filterString}&metadata=count",
               cancellationToken: cancellationToken
           );

       var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateInvoice>(
           serviceProvider,
           requestContext.Server,
           baseUrl,
           filterString,
           pageNum => $"Downloading invoices (page {pageNum})",
           requestContext,
           cancellationToken: cancellationToken
       );

       return new SimplicateData<SimplicateInvoice>()
       {
           Data = items.Skip(offset ?? 0).Take(limit ?? int.MaxValue),
           Metadata = new()
           {
               Count = items.Count,
               Offset = offset ?? null,
               Limit = limit ?? null
           }

       };
   });
}

