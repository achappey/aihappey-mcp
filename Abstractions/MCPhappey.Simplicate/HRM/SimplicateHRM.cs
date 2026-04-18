using System.ComponentModel;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.HRM;

public static partial class SimplicateHRM
{
    [Description("Create a new employee in Simplicate HRM")]
    [McpServerTool(Title = "Create new employee in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_CreateEmployee(
       [Description("The person id of the new employee.")] string personId,
       [Description("The id of the supervisor employee.")] string supervisorId,
       [Description("The id of the new employee status.")] string statusId,
       IServiceProvider serviceProvider,
       RequestContext<CallToolRequestParams> requestContext,
       CancellationToken cancellationToken = default) => await serviceProvider.PostSimplicateResourceAsync(
               requestContext,
               "/hrm/employee",
              new SimplicateNewEmployee
              {
                  PersonId = personId,
                  StatusId = statusId,
                  SupervisorId = supervisorId
              },
               dto => new
               {
                   person_id = dto.PersonId,
                   status = new
                   {
                       id = dto.StatusId
                   },
                   supervisor = new
                   {
                       id = dto.SupervisorId
                   }

               },
               cancellationToken
           );

    [Description("Create a new employee in Simplicate HRM")]
    [McpServerTool(Title = "Create new employee in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_UpdateEmployee(
        string employeeId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The id of the new supervisor employee.")] string? supervisorId = null,
        [Description("The id of the new employee status.")] string? statusId = null,
        CancellationToken cancellationToken = default) => await serviceProvider.PutSimplicateResourceMergedAsync(
        requestContext,
        "/hrm/employee/" + employeeId,
       new SimplicateNewEmployee
       {
           StatusId = statusId,
           SupervisorId = supervisorId
       },
        dto => new
        {
            person_id = dto.PersonId,
            status = new
            {
                id = dto.StatusId
            },
            supervisor = new
            {
                id = dto.SupervisorId
            }

        },
        cancellationToken
    );

    [Description("Get Simplicate employees")]
    [McpServerTool(
       Title = "Get Simplicate employees",
       Name = "simplicate_hrm_get_team_employees",
       OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateHRM_GetTeamEmployees(
           [Description("Team to get the leave totals for")] string teamName,
           IServiceProvider serviceProvider,
           RequestContext<CallToolRequestParams> requestContext,
           [Description("Filter on employment status")] string? employmentStatus = "active",
           [Description("Filter on is user")] bool isUser = true,
           CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
       {
           var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
           var downloadService = serviceProvider.GetRequiredService<DownloadService>();

           // 1) Employees (IDs only)
           var employees = await downloadService.GetAllSimplicatePagesAsync<SimplicateEmployee>(
               serviceProvider, requestContext.Server,
               simplicateOptions.GetApiUrl("/hrm/employee"),
               string.Join("&", new[]
               {
                    $"q[employment_status]={employmentStatus}",
                    $"q[teams.name]=*{teamName}*",
                    $"q[is_user]={isUser.ToString()?.ToLower()}",
                    "sort=name"
               }),
               page => $"Downloading employees {teamName} page {page}",
               requestContext, cancellationToken: cancellationToken);

           return new SimplicateData<SimplicateEmployee>()
           {
               Data = employees,
               Metadata = new SimplicateMetadata()
               {
                   Count = employees.Count,
               }
           };
       });

}

