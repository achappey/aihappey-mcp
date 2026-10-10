using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Hours.Models;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Hours;

public static partial class SimplicateHours
{
    [Description("Get registered hours in Simplicate optionally filtered by date range, project, or employee.")]
    [McpServerTool(Title = "Get Simplicate hours",
           Name = "simplicate_hours_get_hours",
           UseStructuredContent = true,
           OutputSchemaType = typeof(SimplicateData<SimplicateHourItem>),
           OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateHours_GetHours(
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Start date for filtering (inclusive), format yyyy-MM-dd. Optional.")] string? fromDate = null,
           [Description("End date for filtering (inclusive), format yyyy-MM-dd. Optional.")] string? toDate = null,
           [Description("Approval status label to filter by. Optional.")] ApprovalStatusLabel? approvalStatusLabel = null,
           [Description("Invoiced status label to filter by. Optional.")] InvoiceStatus? invoiceStatus = null,
           [Description("Project name to filter by. Optional.")] string? projectName = null,
           [Description("Employee name to filter by. Optional.")] string? employeeName = null,
           [Description("The limit of max allowed results.")] int? limit = null,
           [Description("The offset to search from.")] int? offset = null,
            CancellationToken cancellationToken = default) =>
           await ModelContextToolExtensions.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
           {
               var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
               var downloadService = serviceProvider.GetRequiredService<DownloadService>();
               string baseUrl = simplicateOptions.GetApiUrl("/hours/hours");
               var filters = new List<string>();

               if (!string.IsNullOrWhiteSpace(fromDate))
                   filters.Add($"q[start_date][ge]={Uri.EscapeDataString(fromDate)}");
               if (!string.IsNullOrWhiteSpace(toDate))
                   filters.Add($"q[start_date][le]={Uri.EscapeDataString(toDate)}");

               if (!string.IsNullOrWhiteSpace(projectName)) filters.Add($"q[project.name]=*{Uri.EscapeDataString(projectName)}*");
               if (!string.IsNullOrWhiteSpace(employeeName)) filters.Add($"q[employee.name]=*{Uri.EscapeDataString(employeeName)}*");
               if (approvalStatusLabel.HasValue) filters.Add($"q[approvalstatus.label]=*{Uri.EscapeDataString(approvalStatusLabel.Value.ToString())}*");
               if (invoiceStatus.HasValue) filters.Add($"q[invoice_status]=*{Uri.EscapeDataString(invoiceStatus.Value.ToString())}*");
               if (limit.HasValue) filters.Add($"limit={limit}");
               if (offset.HasValue) filters.Add($"offset={offset}");

               var filterString = string.Join("&", filters);

               if (limit.HasValue && limit.Value <= 100)
                   return await downloadService.GetSimplicatePageAsync<SimplicateHourItem>(
                       serviceProvider,
                       requestContext.Server,
                       $"{baseUrl}?{filterString}&metadata=count",
                       cancellationToken: cancellationToken
                   );

               var items = await downloadService.GetAllSimplicatePagesAsync<SimplicateHourItem>(
                   serviceProvider,
                   requestContext.Server,
                   baseUrl,
                   filterString,
                   pageNum => $"Downloading hours (page {pageNum})",
                   requestContext,
                   cancellationToken: cancellationToken
               );

               return new SimplicateData<SimplicateHourItem>()
               {
                   Data = items.Skip(offset ?? 0).Take(limit ?? int.MaxValue),
                   Metadata = new()
                   {
                       Count = items.Count,
                       Offset = offset ?? null,
                       Limit = limit ?? null
                   }

               };

           }
        ));
}

