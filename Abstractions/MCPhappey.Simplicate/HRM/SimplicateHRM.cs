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

    [Description("Get Simplicate leaves totals grouped on employee and leave type")]
    [McpServerTool(
    Title = "Get Simplicate leave totals",
    Name = "simplicate_hrm_get_leave_totals",
    OpenWorld = false, ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateHRM_GetLeaveTotals(
        [Description("Team to get the leave totals for")] string teamName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Filter on leave type")] string? leaveType = null,
        CancellationToken cancellationToken = default) => 
        await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        // 1) Employees (IDs only)
        var employees = await downloadService.GetAllSimplicatePagesAsync<SimplicateIdItem>(
            serviceProvider, requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/employee"),
            string.Join("&", new[]
            {
            "q[employment_status]=active",
            $"q[teams.name]=*{teamName}",
            "q[is_user]=true",
            "q[type.label]=internal",
            "select=id"
            }),
            page => $"Downloading employees {teamName} page {page}",
            requestContext, cancellationToken: cancellationToken);

        var selectedIds = new HashSet<string>(employees.Select(e => e.Id ?? string.Empty)
                                                      .Where(id => !string.IsNullOrEmpty(id)));

        // 2) Timetables -> average DAILY hours per employee
        var timetables = await downloadService.GetAllSimplicatePagesAsync<SimplicateTimetable>(
            serviceProvider, requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/timetable"),
            string.Join("&", new[] { "q[end_date]=null", "select=even_week,odd_week,employee.,end_date" }),
            page => $"Downloading timetables page {page}",
            requestContext, cancellationToken: cancellationToken);

        // NOTE: We assume SimplicateTimetable.AverageWorkdayHours is "hours per workday".
        // If it is weekly, fix here by converting to daily.
        double CleanDaily(double v) => (v > 0 && v <= 16) ? v : double.NaN;

        var avgDailyHoursByEmp = timetables
            .Where(t => !string.IsNullOrEmpty(t.Employee?.Id))
            .GroupBy(t => t.Employee!.Id)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var daily = g.Select(z => CleanDaily(z.AverageWorkdayHours)).Where(x => !double.IsNaN(x));
                    var avg = daily.Any() ? daily.Average() : double.NaN;
                    // final guardrail: if missing/insane, fallback to 8h/day
                    return double.IsNaN(avg) || avg <= 0 || avg > 16 ? 8.0 : Math.Round(avg, 2);
                });

        // 3) Leaves
        var leaves = await downloadService.GetAllSimplicatePagesAsync<SimplicateLeave>(
            serviceProvider, requestContext.Server,
            simplicateOptions.GetApiUrl("/hrm/leave"),
            "",// "select=employee.,hours,leavetype.,start_date,leave_status.",
            page => $"Downloading HRM leaves page {page}",
            requestContext, cancellationToken: cancellationToken);

        var dsds = leaves.GroupBy(a => a.LeaveType?.Label).Select(a => a.Key);
        var dsds2 = leaves.GroupBy(a => a.LeaveType?.Label);
        if (!string.IsNullOrWhiteSpace(leaveType))
        {
            leaves = [.. leaves.Where(a =>
            a.LeaveType?.Label?.Contains(leaveType, StringComparison.OrdinalIgnoreCase) == true)];
        }

        // 4) Shape output
        var result = leaves
            .Where(a => a.Employee?.Id != null && selectedIds.Contains(a.Employee.Id))
            .GroupBy(x => new
            {
                EmployeeId = x.Employee!.Id,
                EmployeeName = x.Employee!.Name ?? ""
            })
            //.OrderBy(g => g.Key.EmployeeName)
            .Select(empGroup =>
            {
                // stable, per-employee daily hours divisor
                var avgHours = avgDailyHoursByEmp.TryGetValue(empGroup.Key.EmployeeId, out var h) ? h : 8.0;

                // compute "planned" against UTC now (avoid local/offset mismatches)
                var nowUtc = DateTime.UtcNow;

                var leavesByType = empGroup
                    .GroupBy(x => x.LeaveType?.Label ?? "")
                    .Select(lg =>
                    {
                        var sumHours = lg.Sum(x => x.Hours);
                        var plannedHours = lg
                            .Where(a =>
                            {
                                if (string.IsNullOrEmpty(a.StartDate)) return false;
                                if (!DateTimeOffset.TryParse(a.StartDate, out var dto)) return false;
                                return dto.UtcDateTime > nowUtc;
                            })
                            .Sum(x => x.Hours);

                        return new LeaveTotals
                        {
                            // LeaveType = lg.Key,            // NOTE: uses DTO property name (lower field is auto-gen’d)
                            LeaveType = lg.Key,            // set both in case of older serializers
                            TotalHours = sumHours,
                            TotalHoursPlanned = plannedHours,
                            TotalDays = Math.Round(sumHours / avgHours, 2)
                        };
                    })
                    .ToList();

                return new
                {
                    employee_name = empGroup.Key.EmployeeName,
                    avgWorkdayHours = avgHours,      // 👈 include for quick sanity check in UI
                    leaves = leavesByType
                };
            })
            .ToList();

        return new { leaves = result };
    });

    /*
        [Description("Get Simplicate leaves totals grouped on employee and leave type")]
        [McpServerTool(Title = "Get Simplicate leave totals",
            Name = "simplicate_hrm_get_leave_totals",
            OpenWorld = false, ReadOnly = true, UseStructuredContent = true)]
        public static async Task<CallToolResult?> SimplicateHRM_GetLeaveTotals2(
            [Description("Team to get the leave totals for")] string teamName,
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Filter on leave type")] string? leaveType = null,
            CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
        {
            var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            string employeeUrl = simplicateOptions.GetApiUrl("/hrm/employee");

            string employeeSelect = "id";
            var employeeFilters = new List<string>
            {
                "q[employment_status]=active",
                $"q[teams.name]=*{teamName}",
                "q[is_user]=true",
                "q[type.label]=internal",
                $"select={employeeSelect}"
            };

            var employeeFilterString = string.Join("&", employeeFilters);
            var employees = await downloadService.GetAllSimplicatePagesAsync<SimplicateIdItem>(
                      serviceProvider,
                      requestContext.Server,
                      employeeUrl,
                      employeeFilterString,
                      pageNum => $"Downloading employees {teamName} leaves page {pageNum}",
                      requestContext,
                      cancellationToken: cancellationToken
                  );

            var selectedId = employees.OfType<SimplicateIdItem>().Select(a => a.Id);
            string timetableUrl = simplicateOptions.GetApiUrl("/hrm/timetable");
            string timetableSelect = "even_week,odd_week,employee.,end_date";
            var timetableFilters = new List<string>
            {
                $"q[end_date]=null",
                $"select={timetableSelect}"
            };

            var timetableFilterString = string.Join("&", timetableFilters);
            var timetables = await downloadService.GetAllSimplicatePagesAsync<SimplicateTimetable>(
                      serviceProvider,
                      requestContext.Server,
                      timetableUrl,
                      timetableFilterString,
                      pageNum => $"Downloading timetables page {pageNum}",
                      requestContext,
                      cancellationToken: cancellationToken
                  );

            string baseUrl = simplicateOptions.GetApiUrl("/hrm/leave");
            string select = "employee.,hours,leavetype.,start_date";
            var filters = new List<string>
            {
                $"select={select}"
            };

            var filterString = string.Join("&", filters);

            // Typed DTO ophalen via extension method
            var leaves = await downloadService.GetAllSimplicatePagesAsync<SimplicateLeave>(
                serviceProvider,
                requestContext.Server,
                baseUrl,
                filterString,
                pageNum => $"Downloading HRM leaves page {pageNum}",
                requestContext, // Geen requestContext want geen progress nodig, of voeg eventueel toe!
                cancellationToken: cancellationToken
            );

            // Extra filter op leaveType (optioneel)
            if (!string.IsNullOrEmpty(leaveType))
            {
                leaves = [.. leaves.Where(a => a.LeaveType?.Label?.Contains(leaveType, StringComparison.OrdinalIgnoreCase) == true)];
            }

            var timetableLookup = timetables
                  .GroupBy(t => t.Employee.Id)
                  .ToDictionary(g => g.Key, g => g.Average(z => z.AverageWorkdayHours));

            return new
            {
                leaves = leaves
               .Where(a => selectedId.Contains(a.Employee?.Id))
               .GroupBy(x => new
               {
                   EmployeeId = x.Employee?.Id,
                   EmployeeName = x.Employee?.Name ?? "",
                   AverageWorkdayHours = timetableLookup.TryGetValue(x.Employee?.Id ?? "", out var t)
                       ? t : 0
               })
               .OrderBy(x => x.Key.EmployeeName)
               .Select(empGroup =>
               {
                   var avgHours = empGroup.Key.AverageWorkdayHours;

                   return new
                   {
                       employee_name = empGroup.Key.EmployeeName,
                       leaves = empGroup
                           .GroupBy(x => x.LeaveType?.Label ?? "")
                           .Select(lg => new LeaveTotals
                           {
                               LeaveType = lg.Key,
                               TotalHours = lg.Sum(x => x.Hours),
                               TotalHoursPlanned = lg
                                   .Where(a => a.StartDate != null && DateTime.Parse(a.StartDate) > DateTime.Now)
                                   .Sum(x => x.Hours),
                               TotalDays = avgHours > 0 ? lg.Sum(x => x.Hours) / avgHours : 0
                           })
                           .ToList()
                   };
               })
            };


        });*/
}

