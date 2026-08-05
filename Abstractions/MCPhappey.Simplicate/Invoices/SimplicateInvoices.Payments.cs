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
        UseStructuredContent = true,
        OutputSchemaType = typeof(SimplicateData<SimplicateInvoice>),
        ReadOnly = true)]
    [Description("Returns a list of payments received.")]
    public static async Task<CallToolResult?> SimplicateInvoices_GetPayments(
         IServiceProvider serviceProvider,
         RequestContext<CallToolRequestParams> requestContext,
         [Description("Payment date (YYY-MM-DD)")] string paymentDate,
         CancellationToken cancellationToken = default) =>
         await ModelContextToolExtensions.WithExceptionCheck(async () =>
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

       return new SimplicateData<SimplicatePayment>()
       {
           Data = payments
       };
   }));
   
}

