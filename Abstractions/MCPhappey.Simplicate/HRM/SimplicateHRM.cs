using System.ComponentModel;
using System.Globalization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
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
    [Description("Get all Simplicate leave hours for one employee grouped by year")]
    [McpServerTool(
        Title = "Get Simplicate employee leave hours by year",
        Name = "simplicate_hrm_get_employee_leave_hours_by_year",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> SimplicateHRM_GetEmployeeLeaveHoursByYear(
        [Description("Simplicate employee id")] string employeeId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) => await requestContext.WithStructuredContent(async () =>
    {
        var simplicateOptions = serviceProvider.GetRequiredService<SimplicateOptions>();
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        employeeId = employeeId?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(employeeId))
            throw new ArgumentException("employeeId is required", nameof(employeeId));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var encodedEmployeeId = Uri.EscapeDataString(employeeId);

        DateOnly? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
                return dateOnly;

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
                return DateOnly.FromDateTime(dto.LocalDateTime);

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime))
                return DateOnly.FromDateTime(dateTime);

            return null;
        }

        var leaveUrl = simplicateOptions.GetApiUrl("/hrm/leave");

        var leaveSelect = "id,employee.,leavetype.,leave_status.,start_date,end_date,year,hours,description,created_at,updated_at";

        var leaveFilterString = string.Join("&", new[]
        {
            $"q[employee.id]={encodedEmployeeId}",
            $"select={leaveSelect}"
        });

        var leaves = await downloadService.GetAllSimplicatePagesAsync<SimplicateLeaveRecord>(
            serviceProvider,
            requestContext.Server,
            leaveUrl,
            leaveFilterString,
            pageNum => $"Downloading Simplicate leave for employee {employeeId} page {pageNum}",
            requestContext,
            cancellationToken: cancellationToken);

        var rows = leaves
            .Where(x => string.Equals(x.Employee?.Id, employeeId, StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                var startDate = ParseDate(x.StartDate);
                var endDate = ParseDate(x.EndDate);
                var year = x.Year ?? startDate?.Year;

                return year is null
                    ? null
                    : new LeaveRow(
                        Source: x,
                        Year: year.Value,
                        StartDate: startDate,
                        EndDate: endDate,
                        AffectsBalance: x.LeaveType?.AffectsBalance == true);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var years = rows
            .Select(x => x.Year)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var yearlyTotals = years.Select(year =>
        {
            var yearRows = rows
                .Where(x => x.Year == year)
                .OrderBy(x => x.StartDate)
                .ThenBy(x => x.Source.CreatedAt)
                .ToList();

            var balanceCredits = yearRows
                .Where(x => x.AffectsBalance && x.Source.Hours > 0)
                .ToList();

            var balanceTaken = yearRows
                .Where(x => x.AffectsBalance && x.Source.Hours < 0)
                .ToList();

            var allCredits = yearRows
                .Where(x => x.Source.Hours > 0)
                .ToList();

            var allTaken = yearRows
                .Where(x => x.Source.Hours < 0)
                .ToList();

            double? takenUntilTodayHours = null;
            double? takenAfterTodayHours = null;

            if (year == today.Year)
            {
                takenUntilTodayHours = Math.Round(
                    -balanceTaken
                        .Where(x => x.StartDate is not null && x.StartDate.Value <= today)
                        .Sum(x => x.Source.Hours),
                    2);

                takenAfterTodayHours = Math.Round(
                    -balanceTaken
                        .Where(x => x.StartDate is not null && x.StartDate.Value > today)
                        .Sum(x => x.Source.Hours),
                    2);
            }

            var creditedHours = Math.Round(balanceCredits.Sum(x => x.Source.Hours), 2);
            var takenHours = Math.Round(-balanceTaken.Sum(x => x.Source.Hours), 2);

            return new
            {
                year,

                credited_hours = creditedHours,
                taken_hours = takenHours,
                remaining_hours_calculated = Math.Round(creditedHours - takenHours, 2),

                taken_until_today_hours = takenUntilTodayHours,
                taken_after_today_hours = takenAfterTodayHours,

                all_credited_hours_including_non_balance = Math.Round(allCredits.Sum(x => x.Source.Hours), 2),
                all_taken_hours_including_non_balance = Math.Round(-allTaken.Sum(x => x.Source.Hours), 2),

                credited_by_leave_type = balanceCredits
                    .GroupBy(x => new
                    {
                        LeaveTypeId = x.Source.LeaveType?.Id ?? "",
                        LeaveTypeLabel = x.Source.LeaveType?.Label ?? ""
                    })
                    .Select(g => new
                    {
                        leave_type_id = g.Key.LeaveTypeId,
                        leave_type = g.Key.LeaveTypeLabel,
                        credited_hours = Math.Round(g.Sum(x => x.Source.Hours), 2)
                    })
                    .OrderBy(x => x.leave_type)
                    .ToList(),

                taken_by_leave_type = balanceTaken
                    .GroupBy(x => new
                    {
                        LeaveTypeId = x.Source.LeaveType?.Id ?? "",
                        LeaveTypeLabel = x.Source.LeaveType?.Label ?? ""
                    })
                    .Select(g => new
                    {
                        leave_type_id = g.Key.LeaveTypeId,
                        leave_type = g.Key.LeaveTypeLabel,
                        taken_hours = Math.Round(-g.Sum(x => x.Source.Hours), 2)
                    })
                    .OrderBy(x => x.leave_type)
                    .ToList(),

                non_balance_taken_by_leave_type = yearRows
                    .Where(x => !x.AffectsBalance && x.Source.Hours < 0)
                    .GroupBy(x => new
                    {
                        LeaveTypeId = x.Source.LeaveType?.Id ?? "",
                        LeaveTypeLabel = x.Source.LeaveType?.Label ?? ""
                    })
                    .Select(g => new
                    {
                        leave_type_id = g.Key.LeaveTypeId,
                        leave_type = g.Key.LeaveTypeLabel,
                        taken_hours = Math.Round(-g.Sum(x => x.Source.Hours), 2)
                    })
                    .OrderBy(x => x.leave_type)
                    .ToList(),
            };
        }).ToList();

        return new
        {
            employee_id = employeeId,
            today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            source = "/hrm/leave",

      
            years = yearlyTotals
        };
    });



    [Description("Create a new employee in Simplicate HRM")]
    [McpServerTool(
        Title = "Create new employee in Simplicate",
        Name = "simplicate_hrm_create_employee",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_CreateEmployee(
        [Description("The person id of the new employee.")] string personId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The supervisor employee id.")] string? supervisorId = null,
        [Description("The employee status id.")] string? statusId = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/hrm/employee",
            new SimplicateNewEmployee
            {
                PersonId = personId,
                StatusId = statusId,
                SupervisorId = supervisorId
            },
            MapEmployeeToWriteBody,
            GetEmployeeWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Update an existing employee in Simplicate HRM")]
    [McpServerTool(
        Title = "Update employee in Simplicate",
        Name = "simplicate_hrm_update_employee",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_UpdateEmployee(
        [Description("The Simplicate employee id.")] string employeeId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The person id of the employee.")] string? personId = null,
        [Description("The id of the new supervisor employee.")] string? supervisorId = null,
        [Description("The id of the new employee status.")] string? statusId = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateEmployee, SimplicateNewEmployee>(
            requestContext,
            "/hrm/employee/" + employeeId,
            new SimplicateNewEmployee
            {
                PersonId = personId ?? string.Empty,
                StatusId = statusId,
                SupervisorId = supervisorId
            },
            (_, dto) => MapEmployeeToWriteBody(dto),
            MapEmployeeToWriteModel,
            GetEmployeeWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Create a new absence entry in Simplicate HRM")]
    [McpServerTool(
        Title = "Create absence in Simplicate",
        Name = "simplicate_hrm_create_absence",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_CreateAbsence(
        [Description("Absence start date.")] string startDate,
        [Description("Absence end date.")] string endDate,
        [Description("Calendar year of the absence.")] int year,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional description for the absence.")] string? description = null,
        [Description("The employee id.")] string? employeeId = null,
        [Description("The absence type id.")] string? absenceTypeId = null,
        [Description("Whether the absence is time defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/hrm/absence",
            new SimplicateNewAbsence
            {
                StartDate = startDate,
                EndDate = endDate,
                Year = year,
                Description = description,
                EmployeeId = employeeId,
                AbsenceTypeId = absenceTypeId,
                IsTimeDefined = isTimeDefined
            },
            MapAbsenceToWriteBody,
            GetAbsenceWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Update an existing absence entry in Simplicate HRM")]
    [McpServerTool(
        Title = "Update absence in Simplicate",
        Name = "simplicate_hrm_update_absence",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_UpdateAbsence(
        [Description("The Simplicate absence id.")] string absenceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Absence start date.")] string? startDate = null,
        [Description("Absence end date.")] string? endDate = null,
        [Description("Calendar year of the absence.")] int? year = null,
        [Description("Optional description for the absence.")] string? description = null,
        [Description("The employee id.")] string? employeeId = null,
        [Description("The absence type id.")] string? absenceTypeId = null,
        [Description("Whether the absence is time defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateAbsence, SimplicateNewAbsence>(
            requestContext,
            "/hrm/absence/" + absenceId,
            new SimplicateNewAbsence
            {
                StartDate = startDate,
                EndDate = endDate,
                Year = year,
                Description = description,
                EmployeeId = employeeId,
                AbsenceTypeId = absenceTypeId,
                IsTimeDefined = isTimeDefined
            },
            (_, dto) => MapAbsenceToWriteBody(dto),
            MapAbsenceToWriteModel,
            GetAbsenceWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Delete an absence entry in Simplicate HRM after typed confirmation of the exact absence id.")]
    [McpServerTool(
        Title = "Delete absence in Simplicate",
        Name = "simplicate_hrm_delete_absence",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_DeleteAbsence(
        [Description("The Simplicate absence id.")] string absenceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateAbsence>(
            expectedName: absenceId,
            async ct => await serviceProvider.DeleteSimplicateResourceAsync(
                requestContext,
                "/hrm/absence/" + absenceId,
                $"Absence '{absenceId}' deleted.",
                ct),
            $"Absence '{absenceId}' deleted.",
            cancellationToken);

    [Description("Create a new leave entry in Simplicate HRM")]
    [McpServerTool(
        Title = "Create leave in Simplicate",
        Name = "simplicate_hrm_create_leave",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_CreateLeave(
        [Description("Leave start date.")] string startDate,
        [Description("Leave end date.")] string endDate,
        [Description("Calendar year of the leave.")] int year,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Number of leave hours.")] double? hours = null,
        [Description("Optional description for the leave.")] string? description = null,
        [Description("The employee id.")] string? employeeId = null,
        [Description("The leave type id.")] string? leaveTypeId = null,
        [Description("Whether the leave is time defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/hrm/leave",
            new SimplicateNewLeave
            {
                StartDate = startDate,
                EndDate = endDate,
                Year = year,
                Hours = hours,
                Description = description,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                IsTimeDefined = isTimeDefined
            },
            MapLeaveToWriteBody,
            GetLeaveWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Update an existing leave entry in Simplicate HRM")]
    [McpServerTool(
        Title = "Update leave in Simplicate",
        Name = "simplicate_hrm_update_leave",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_UpdateLeave(
        [Description("The Simplicate leave id.")] string leaveId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Leave start date.")] string? startDate = null,
        [Description("Leave end date.")] string? endDate = null,
        [Description("Calendar year of the leave.")] int? year = null,
        [Description("Number of leave hours.")] double? hours = null,
        [Description("Optional description for the leave.")] string? description = null,
        [Description("The employee id.")] string? employeeId = null,
        [Description("The leave type id.")] string? leaveTypeId = null,
        [Description("Whether the leave is time defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateLeave, SimplicateNewLeave>(
            requestContext,
            "/hrm/leave/" + leaveId,
            new SimplicateNewLeave
            {
                StartDate = startDate,
                EndDate = endDate,
                Year = year,
                Hours = hours,
                Description = description,
                EmployeeId = employeeId,
                LeaveTypeId = leaveTypeId,
                IsTimeDefined = isTimeDefined
            },
            (_, dto) => MapLeaveToWriteBody(dto),
            MapLeaveToWriteModel,
            GetLeaveWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Create a new timetable in Simplicate HRM")]
    [McpServerTool(
        Title = "Create timetable in Simplicate",
        Name = "simplicate_hrm_create_timetable",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_CreateTimetable(
        [Description("The employee id.")] string employeeId,
        [Description("Timetable start date.")] string startDate,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Timetable end date.")] string? endDate = null,
        [Description("Productivity target.")] int? productivityTarget = null,
        [Description("Whether the employee should write hours.")] bool? shouldWriteHours = null,
        [Description("Whether the timetable has a separate odd week schedule.")] bool? hasOddWeeks = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/hrm/timetable",
            new SimplicateNewTimetable
            {
                EmployeeId = employeeId,
                StartDate = startDate,
                EndDate = endDate,
                ProductivityTarget = productivityTarget,
                ShouldWriteHours = shouldWriteHours,
                HasOddWeeks = hasOddWeeks,
                EvenWeek = CreateEmptyWeekSchedule(),
                OddWeek = CreateEmptyWeekSchedule()
            },
            MapTimetableToWriteBody,
            GetTimetableWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Update an existing timetable in Simplicate HRM")]
    [McpServerTool(
        Title = "Update timetable in Simplicate",
        Name = "simplicate_hrm_update_timetable",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_UpdateTimetable(
        [Description("The Simplicate timetable id.")] string timetableId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("The employee id.")] string? employeeId = null,
        [Description("Timetable start date.")] string? startDate = null,
        [Description("Timetable end date.")] string? endDate = null,
        [Description("Productivity target.")] int? productivityTarget = null,
        [Description("Whether the employee should write hours.")] bool? shouldWriteHours = null,
        [Description("Whether the timetable has a separate odd week schedule.")] bool? hasOddWeeks = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateTimetable, SimplicateNewTimetable>(
            requestContext,
            "/hrm/timetable/" + timetableId,
            new SimplicateNewTimetable
            {
                EmployeeId = employeeId,
                StartDate = startDate,
                EndDate = endDate,
                ProductivityTarget = productivityTarget,
                ShouldWriteHours = shouldWriteHours,
                HasOddWeeks = hasOddWeeks,
                EvenWeek = CreateEmptyWeekSchedule(),
                OddWeek = CreateEmptyWeekSchedule()
            },
            (_, dto) => MapTimetableToWriteBody(dto),
            MapTimetableToWriteModel,
            GetTimetableWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Delete a timetable in Simplicate HRM after typed confirmation of the exact timetable id.")]
    [McpServerTool(
        Title = "Delete timetable in Simplicate",
        Name = "simplicate_hrm_delete_timetable",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHRM_DeleteTimetable(
        [Description("The Simplicate timetable id.")] string timetableId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateTimetable>(
            expectedName: timetableId,
            async ct => await serviceProvider.DeleteSimplicateResourceAsync(
                requestContext,
                "/hrm/timetable/" + timetableId,
                $"Timetable '{timetableId}' deleted.",
                ct),
            $"Timetable '{timetableId}' deleted.",
            cancellationToken);

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

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetEmployeeWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewEmployee dto,
            CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var personOverrides = await serviceProvider.BuildSimplicatePersonElicitOverridesAsync<SimplicateNewEmployee>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewEmployee.PersonId),
                    Title = "Person",
                    Description = "Select the person record that should become the employee.",
                    DefaultValue = dto.PersonId
                }
            ],
            cancellationToken);

        foreach (var personOverride in personOverrides)
            overrides[personOverride.Key] = personOverride.Value;

        var supervisorOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewEmployee>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewEmployee.SupervisorId),
                    Title = "Supervisor",
                    Description = "Select the supervisor employee.",
                    DefaultValue = dto.SupervisorId
                }
            ],
            cancellationToken);

        foreach (var supervisorOverride in supervisorOverrides)
            overrides[supervisorOverride.Key] = supervisorOverride.Value;

        var statusOverrides = await serviceProvider.BuildSimplicateEmployeeStatusElicitOverridesAsync<SimplicateNewEmployee>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewEmployee.StatusId),
                    Title = "Employee status",
                    Description = "Select the employee status.",
                    DefaultValue = dto.StatusId
                }
            ],
            cancellationToken);

        foreach (var statusOverride in statusOverrides)
            overrides[statusOverride.Key] = statusOverride.Value;

        return overrides;
    }

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetAbsenceWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewAbsence dto,
            CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewAbsence>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewAbsence.EmployeeId),
                    Title = "Employee",
                    Description = "Select the employee for this absence.",
                    DefaultValue = dto.EmployeeId
                }
            ],
            cancellationToken);

        foreach (var employeeOverride in employeeOverrides)
            overrides[employeeOverride.Key] = employeeOverride.Value;

        var absenceTypeOverrides = await serviceProvider.BuildSimplicateAbsenceTypeElicitOverridesAsync<SimplicateNewAbsence>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewAbsence.AbsenceTypeId),
                    Title = "Absence type",
                    Description = "Select the absence type.",
                    DefaultValue = dto.AbsenceTypeId
                }
            ],
            cancellationToken);

        foreach (var absenceTypeOverride in absenceTypeOverrides)
            overrides[absenceTypeOverride.Key] = absenceTypeOverride.Value;

        return overrides;
    }

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetLeaveWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewLeave dto,
            CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewLeave>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewLeave.EmployeeId),
                    Title = "Employee",
                    Description = "Select the employee for this leave entry.",
                    DefaultValue = dto.EmployeeId
                }
            ],
            cancellationToken);

        foreach (var employeeOverride in employeeOverrides)
            overrides[employeeOverride.Key] = employeeOverride.Value;

        var leaveTypeOverrides = await serviceProvider.BuildSimplicateLeaveTypeElicitOverridesAsync<SimplicateNewLeave>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewLeave.LeaveTypeId),
                    Title = "Leave type",
                    Description = "Select the leave type.",
                    DefaultValue = dto.LeaveTypeId
                }
            ],
            cancellationToken);

        foreach (var leaveTypeOverride in leaveTypeOverrides)
            overrides[leaveTypeOverride.Key] = leaveTypeOverride.Value;

        return overrides;
    }

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetTimetableWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateNewTimetable dto,
            CancellationToken cancellationToken)
    {
        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewTimetable>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateNewTimetable.EmployeeId),
                    Title = "Employee",
                    Description = "Select the employee for this timetable.",
                    DefaultValue = dto.EmployeeId
                }
            ],
            cancellationToken);

        return new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(employeeOverrides, StringComparer.OrdinalIgnoreCase);
    }

    private static SimplicateNewEmployee MapEmployeeToWriteModel(SimplicateEmployee employee)
        => new()
        {
            PersonId = employee.PersonId,
            SupervisorId = employee.Supervisor?.Id,
            StatusId = employee.Status?.Id,
            CustomFields = employee.CustomFields?
                .Where(field => !string.IsNullOrWhiteSpace(field.Name))
                .Select(field => new SimplicateCustomFieldValue
                {
                    Name = field.Name,
                    Value = field.Value
                })
                .ToList()
        };

    private static SimplicateNewAbsence MapAbsenceToWriteModel(SimplicateAbsence absence)
        => new()
        {
            StartDate = absence.StartDate,
            EndDate = absence.EndDate,
            Year = absence.Year,
            Description = absence.Description,
            EmployeeId = absence.Employee?.Id,
            AbsenceTypeId = absence.AbsenceType?.Id,
            IsTimeDefined = absence.IsTimeDefined
        };

    private static SimplicateNewLeave MapLeaveToWriteModel(SimplicateLeave leave)
        => new()
        {
            StartDate = leave.StartDate,
            EndDate = leave.EndDate,
            Year = leave.Year,
            Hours = leave.Hours,
            Description = leave.Description,
            EmployeeId = leave.Employee?.Id,
            LeaveTypeId = leave.LeaveType?.Id,
            IsTimeDefined = leave.IsTimeDefined
        };

    private static SimplicateNewTimetable MapTimetableToWriteModel(SimplicateTimetable timetable)
        => new()
        {
            EmployeeId = timetable.Employee?.Id,
            StartDate = timetable.StartDate,
            EndDate = timetable.EndDate,
            ProductivityTarget = timetable.ProductivityTarget,
            ShouldWriteHours = timetable.ShouldWriteHours,
            HasOddWeeks = timetable.HasOddWeeks,
            EvenWeek = CloneWeekSchedule(timetable.EvenWeek),
            OddWeek = CloneWeekSchedule(timetable.OddWeek)
        };

    private static object MapEmployeeToWriteBody(SimplicateNewEmployee dto)
        => new
        {
            person_id = dto.PersonId,
            status = !string.IsNullOrWhiteSpace(dto.StatusId)
                ? new
                {
                    id = dto.StatusId
                }
                : null,
            supervisor = !string.IsNullOrWhiteSpace(dto.SupervisorId)
                ? new
                {
                    id = dto.SupervisorId
                }
                : null,
            custom_fields = dto.CustomFields?
                .Where(field => !string.IsNullOrWhiteSpace(field.Name))
                .Select(field => new
                {
                    name = field.Name,
                    value = field.Value
                })
                .ToList()
        };

    private static object MapAbsenceToWriteBody(SimplicateNewAbsence dto)
        => new
        {
            start_date = dto.StartDate,
            end_date = dto.EndDate,
            year = dto.Year,
            description = dto.Description,
            employee_id = dto.EmployeeId,
            absence_type_id = dto.AbsenceTypeId,
            is_time_defined = dto.IsTimeDefined
        };

    private static object MapLeaveToWriteBody(SimplicateNewLeave dto)
        => new
        {
            start_date = dto.StartDate,
            end_date = dto.EndDate,
            year = dto.Year,
            hours = dto.Hours,
            description = dto.Description,
            employee_id = dto.EmployeeId,
            leave_type_id = dto.LeaveTypeId,
            is_time_defined = dto.IsTimeDefined
        };

    private static object MapTimetableToWriteBody(SimplicateNewTimetable dto)
        => new
        {
            even_week = dto.EvenWeek,
            odd_week = dto.OddWeek,
            start_date = dto.StartDate,
            end_date = dto.EndDate,
            productivity_target = dto.ProductivityTarget,
            should_write_hours = dto.ShouldWriteHours,
            employee_id = dto.EmployeeId,
            has_odd_weeks = dto.HasOddWeeks
        };

    private static WeekSchedule CreateEmptyWeekSchedule()
        => new()
        {
            Day1 = new DaySchedule(),
            Day2 = new DaySchedule(),
            Day3 = new DaySchedule(),
            Day4 = new DaySchedule(),
            Day5 = new DaySchedule(),
            Day6 = new DaySchedule(),
            Day7 = new DaySchedule()
        };

    private static WeekSchedule CloneWeekSchedule(WeekSchedule? schedule)
        => new()
        {
            Day1 = CloneDaySchedule(schedule?.Day1),
            Day2 = CloneDaySchedule(schedule?.Day2),
            Day3 = CloneDaySchedule(schedule?.Day3),
            Day4 = CloneDaySchedule(schedule?.Day4),
            Day5 = CloneDaySchedule(schedule?.Day5),
            Day6 = CloneDaySchedule(schedule?.Day6),
            Day7 = CloneDaySchedule(schedule?.Day7)
        };

    private static DaySchedule CloneDaySchedule(DaySchedule? schedule)
        => new()
        {
            StartTime = schedule?.StartTime ?? 0,
            EndTime = schedule?.EndTime ?? 0,
            Hours = schedule?.Hours ?? 0
        };

}

[Description("Please confirm deletion of the Simplicate absence id: {0}")]
public sealed class ConfirmDeleteSimplicateAbsence : IHasName
{
    [Description("Type the exact absence id to confirm deletion.")]
    public string? Name { get; set; }
}

[Description("Please confirm deletion of the Simplicate timetable id: {0}")]
public sealed class ConfirmDeleteSimplicateTimetable : IHasName
{
    [Description("Type the exact timetable id to confirm deletion.")]
    public string? Name { get; set; }
}
