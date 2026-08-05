using System.ComponentModel;
using MCPhappey.Common.Extensions;
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
    [Description("Create a new hour registration in Simplicate")]
    [McpServerTool(Title = "Create hour registration", OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHours_CreateHourRegistration(
     [Description("The number of hours.")] double hours,
     [Description("The id of the employee.")] string employeeId,
     [Description("The id of the project.")] string projectId,
     [Description("The id of the project service.")] string projectServiceId,
     [Description("The id of the hour type.")] string hourTypeId,
     [Description("The start date of the hour registration.")] DateTime startDate,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default)
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();

        if (employeeId.StartsWith("employee:") == false)
        {
            return $"Invalid employee id. Expected format: 'employee:[unique_id]'. Use resource https://{simplicateOptions.Organization}.simplicate.app/api/v2/hrm/employee?q[name]=*[nameFilter]* to search by name.".ToErrorCallToolResponse();
        }

        if (projectId.StartsWith("project:") == false)
        {
            return $"Invalid project id. Expected format: 'project:[unique_id]'. Use resource https://{simplicateOptions.Organization}.simplicate.app/api/v2/projects/project?q[name]=*[nameFilter]* to search by name.".ToErrorCallToolResponse();
        }

        if (projectServiceId.StartsWith("service:") == false)
        {
            return $"Invalid projectServiceId id. Expected format: 'service:[unique_id]'. Use resource https://{simplicateOptions.Organization}.simplicate.app/api/v2/projects/service?q[name]=*[nameFilter]* to search by name.".ToErrorCallToolResponse();
        }

        if (hourTypeId.StartsWith("hourstype:") == false)
        {
            return $"Invalid hourTypeId id. Expected format: 'hourstype:[unique_id]'. Use resource https://{simplicateOptions.Organization}.simplicate.app/api/v2/hours/hourstype?q[label]=*[nameFilter]* to search by name.".ToErrorCallToolResponse();
        }

        // Simplicate Hours endpoint
        string baseUrl = simplicateOptions.GetApiUrl("/hours/hours");

        var dto = new SimplicateNewHour
        {
            Hours = hours,
            StartDate = startDate,
            EmployeeId = employeeId,
            ProjectId = projectId,
            TypeId = hourTypeId,
            ProjectServiceId = projectServiceId,
        };

        // Optionally let user confirm/fill fields in Elicit if you want:
        var (dtoItem, _, _) = await requestContext.Server.TryElicit(dto, cancellationToken);

        return (await serviceProvider.PostSimplicateItemAsync(
            baseUrl,
            dtoItem!,
            requestContext: requestContext,
            cancellationToken: cancellationToken
        ))?.ToCallToolResult();
    }

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

               var filterString = string.Join("&", filters);

               var hours = await downloadService.GetAllSimplicatePagesAsync<SimplicateHourItem>(
                   serviceProvider,
                   requestContext.Server,
                   baseUrl,
                   filterString + "&sort=start_date",
                   pageNum => $"Downloading hours",
                   requestContext,
                   cancellationToken: cancellationToken
               );

               return new SimplicateData<SimplicateHourItem>()
               {
                   Data = hours
               };
           }
        ));
}

