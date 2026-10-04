using System.ComponentModel;
using System.Globalization;
using MCPhappey.Simplicate.Hours.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Hours;

public static partial class SimplicateHours
{
    [Description("Create a new hour registration in Simplicate using primitive values and exact IDs. Supported clients review a lookup-backed form; other clients submit the supplied values directly.")]
    [McpServerTool(Title = "Create hour registration", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_CreateHourRegistration(
        [Description("The number of hours.")] double hours,
        [Description("The id of the employee.")] string employeeId,
        [Description("The id of the project.")] string projectId,
        [Description("The id of the project service.")] string projectServiceId,
        [Description("The id of the hour type.")] string hourTypeId,
        [Description("The start date of the hour registration.")] DateTime startDate,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Registration source.")] string? source = null,
        [Description("Registration note.")] string? note = null,
        [Description("Duration in minutes.")] int? durationInMinutes = null,
        [Description("End date and time, yyyy-MM-dd HH:mm:ss.")] string? endDate = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        [Description("Whether the registration recurs.")] bool? isRecurring = null,
        [Description("Whether the registration originates externally.")] bool? isExternal = null,
        [Description("Whether the hours are billable.")] bool? billable = null,
        [Description("Project assignment ID.")] string? assignmentId = null,
        [Description("Synchronize to Cronofy.")] bool? shouldSyncToCronofy = null,
        [Description("Related external item URL.")] string? externalUrl = null,
        [Description("Approval status ID.")] string? approvalStatusId = null,
        [Description("Registration location.")] string? location = null,
        [Description("Existing address ID.")] string? addressId = null,
        [Description("Nested address type.")] string? addressType = null,
        [Description("First address line.")] string? addressLine1 = null,
        [Description("Second address line.")] string? addressLine2 = null,
        [Description("Address postal code.")] string? addressPostalCode = null,
        [Description("Address province or region.")] string? addressProvince = null,
        [Description("Address city or locality.")] string? addressLocality = null,
        [Description("Address country code.")] string? addressCountryCode = null,
        [Description("Address country ID.")] string? addressCountryId = null,
        [Description("True explicitly clears the nested address; omit or use false to avoid clearing it.")] bool? clearAddress = null,
        [Description("Recurrence ID.")] string? recurrenceId = null,
        [Description("Recurrence update scope or mode.")] string? recurrenceUpdate = null,
        [Description("Recurrence rule start date and time.")] string? recurrenceRruleDtstart = null,
        [Description("Recurrence rule frequency as an integer.")] int? recurrenceRruleFreq = null,
        [Description("Recurrence rule end date and time.")] string? recurrenceRruleUntil = null,
        [Description("Number of recurrence occurrences.")] int? recurrenceRruleCount = null,
        [Description("Recurrence interval.")] int? recurrenceRruleInterval = null,
        [Description("Recurrence week-start day.")] string? recurrenceRruleWkst = null,
        [Description("Recurrence day selection as a scalar string, not JSON.")] string? recurrenceRruleByday = null,
        [Description("Recurrence set-position selection as a scalar string, not JSON.")] string? recurrenceRruleBysetpos = null,
        [Description("Arbitrary external item UID, not a Simplicate record ID.")] string? externalItemId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/hours", null, new SimplicateHourWrite
        {
            Hours = hours, EmployeeId = employeeId, ProjectId = projectId, ProjectServiceId = projectServiceId,
            TypeId = hourTypeId, StartDate = startDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Source = source, Note = note, DurationInMinutes = durationInMinutes, EndDate = endDate,
            IsTimeDefined = isTimeDefined, IsRecurring = isRecurring, IsExternal = isExternal, Billable = billable,
            AssignmentId = assignmentId, ShouldSyncToCronofy = shouldSyncToCronofy, ExternalUrl = externalUrl,
            ApprovalStatusId = approvalStatusId, Location = location, AddressId = addressId, AddressType = addressType,
            AddressLine1 = addressLine1, AddressLine2 = addressLine2, AddressPostalCode = addressPostalCode,
            AddressProvince = addressProvince, AddressLocality = addressLocality, AddressCountryCode = addressCountryCode,
            AddressCountryId = addressCountryId, ClearAddress = clearAddress, RecurrenceId = recurrenceId,
            RecurrenceUpdate = recurrenceUpdate, RecurrenceRruleDtstart = recurrenceRruleDtstart,
            RecurrenceRruleFreq = recurrenceRruleFreq, RecurrenceRruleUntil = recurrenceRruleUntil,
            RecurrenceRruleCount = recurrenceRruleCount, RecurrenceRruleInterval = recurrenceRruleInterval,
            RecurrenceRruleWkst = recurrenceRruleWkst, RecurrenceRruleByday = recurrenceRruleByday,
            RecurrenceRruleBysetpos = recurrenceRruleBysetpos, ExternalItemId = externalItemId
        }, cancellationToken);

    [Description("Update an hour registration in Simplicate. Omitted values are preserved; supplied false and zero values are not omitted. Supported clients review a prefilled form; other clients update supplied values directly.")]
    [McpServerTool(Title = "Update hour registration in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_UpdateHourRegistration(
        [Description("Exact hour registration record ID.")] string hourRegistrationId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Number of registered hours.")] double? hours = null,
        [Description("Employee ID.")] string? employeeId = null,
        [Description("Project ID.")] string? projectId = null,
        [Description("Project service ID.")] string? projectServiceId = null,
        [Description("Hour type ID.")] string? hourTypeId = null,
        [Description("Start date and time, yyyy-MM-dd HH:mm:ss.")] string? startDate = null,
        [Description("Registration source.")] string? source = null,
        [Description("Registration note.")] string? note = null,
        [Description("Duration in minutes.")] int? durationInMinutes = null,
        [Description("End date and time, yyyy-MM-dd HH:mm:ss.")] string? endDate = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        [Description("Whether the registration recurs.")] bool? isRecurring = null,
        [Description("Whether the registration originates externally.")] bool? isExternal = null,
        [Description("Whether the hours are billable.")] bool? billable = null,
        [Description("Project assignment ID.")] string? assignmentId = null,
        [Description("Synchronize to Cronofy.")] bool? shouldSyncToCronofy = null,
        [Description("Related external item URL.")] string? externalUrl = null,
        [Description("Approval status ID.")] string? approvalStatusId = null,
        [Description("Registration location.")] string? location = null,
        [Description("Existing address ID.")] string? addressId = null,
        [Description("Nested address type.")] string? addressType = null,
        [Description("First address line.")] string? addressLine1 = null,
        [Description("Second address line.")] string? addressLine2 = null,
        [Description("Address postal code.")] string? addressPostalCode = null,
        [Description("Address province or region.")] string? addressProvince = null,
        [Description("Address city or locality.")] string? addressLocality = null,
        [Description("Address country code.")] string? addressCountryCode = null,
        [Description("Address country ID.")] string? addressCountryId = null,
        [Description("True explicitly clears the existing nested address; omit or use false to avoid clearing it.")] bool? clearAddress = null,
        [Description("Recurrence ID.")] string? recurrenceId = null,
        [Description("Recurrence update scope or mode.")] string? recurrenceUpdate = null,
        [Description("Recurrence rule start date and time.")] string? recurrenceRruleDtstart = null,
        [Description("Recurrence rule frequency as an integer.")] int? recurrenceRruleFreq = null,
        [Description("Recurrence rule end date and time.")] string? recurrenceRruleUntil = null,
        [Description("Number of recurrence occurrences.")] int? recurrenceRruleCount = null,
        [Description("Recurrence interval.")] int? recurrenceRruleInterval = null,
        [Description("Recurrence week-start day.")] string? recurrenceRruleWkst = null,
        [Description("Recurrence day selection as a scalar string, not JSON.")] string? recurrenceRruleByday = null,
        [Description("Recurrence set-position selection as a scalar string, not JSON.")] string? recurrenceRruleBysetpos = null,
        [Description("Arbitrary external item UID, not a Simplicate record ID.")] string? externalItemId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/hours", hourRegistrationId, new SimplicateHourWrite
        {
            Hours = hours, EmployeeId = employeeId, ProjectId = projectId, ProjectServiceId = projectServiceId,
            TypeId = hourTypeId, StartDate = startDate, Source = source, Note = note,
            DurationInMinutes = durationInMinutes, EndDate = endDate, IsTimeDefined = isTimeDefined,
            IsRecurring = isRecurring, IsExternal = isExternal, Billable = billable, AssignmentId = assignmentId,
            ShouldSyncToCronofy = shouldSyncToCronofy, ExternalUrl = externalUrl, ApprovalStatusId = approvalStatusId,
            Location = location, AddressId = addressId, AddressType = addressType, AddressLine1 = addressLine1,
            AddressLine2 = addressLine2, AddressPostalCode = addressPostalCode, AddressProvince = addressProvince,
            AddressLocality = addressLocality, AddressCountryCode = addressCountryCode, AddressCountryId = addressCountryId,
            ClearAddress = clearAddress, RecurrenceId = recurrenceId, RecurrenceUpdate = recurrenceUpdate,
            RecurrenceRruleDtstart = recurrenceRruleDtstart, RecurrenceRruleFreq = recurrenceRruleFreq,
            RecurrenceRruleUntil = recurrenceRruleUntil, RecurrenceRruleCount = recurrenceRruleCount,
            RecurrenceRruleInterval = recurrenceRruleInterval, RecurrenceRruleWkst = recurrenceRruleWkst,
            RecurrenceRruleByday = recurrenceRruleByday, RecurrenceRruleBysetpos = recurrenceRruleBysetpos,
            ExternalItemId = externalItemId
        }, cancellationToken);

    [Description("Delete an hour registration in Simplicate. Form-capable clients must confirm its ID; clients without form support delete directly using the supplied ID.")]
    [McpServerTool(Title = "Delete hour registration in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult> SimplicateHours_DeleteHourRegistration(
        [Description("Exact hour registration record ID.")] string hourRegistrationId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => DeleteAsync(serviceProvider, requestContext, "/hours/hours", hourRegistrationId, "Hour registration deleted.", cancellationToken);

    [Description("Submit hours for an employee and date range using POST /hours/submit. Supported clients review a form; other clients submit the supplied values directly.")]
    [McpServerTool(Title = "Submit hours in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_SubmitHours(
        [Description("Employee ID.")] string employeeId,
        [Description("Submission start date, yyyy-MM-dd.")] string startDate,
        [Description("Submission end date, yyyy-MM-dd.")] string endDate,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/submit", null, new SimplicateHoursSubmissionWrite
        {
            EmployeeId = employeeId, StartDate = startDate, EndDate = endDate
        }, cancellationToken);

    [Description("Submit employee hours for a date range using POST /hours/submission. Supported clients review a form; other clients submit the supplied values directly.")]
    [McpServerTool(Title = "Submit employee hours in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_SubmitEmployeeHours(
        [Description("Employee ID.")] string employeeId,
        [Description("Submission start date, yyyy-MM-dd.")] string startDate,
        [Description("Submission end date, yyyy-MM-dd.")] string endDate,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/submission", null, new SimplicateHoursSubmissionWrite
        {
            EmployeeId = employeeId, StartDate = startDate, EndDate = endDate
        }, cancellationToken);

    [Description("Create an absence in Simplicate using primitive values and exact IDs. Supported clients review a lookup-backed form; other clients submit supplied values directly.")]
    [McpServerTool(Title = "Create absence in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_CreateAbsence(
        [Description("Employee ID.")] string employeeId,
        [Description("Absence type ID.")] string absenceTypeId,
        [Description("Absence start date and time, yyyy-MM-dd HH:mm:ss.")] string startDate,
        [Description("Absence end date and time, yyyy-MM-dd HH:mm:ss.")] string endDate,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Absence registration year.")] int? year = null,
        [Description("Absence description.")] string? description = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/absence", null, new SimplicateHoursAbsenceWrite
        {
            EmployeeId = employeeId, AbsenceTypeId = absenceTypeId, StartDate = startDate, EndDate = endDate,
            Year = year, Description = description, IsTimeDefined = isTimeDefined
        }, cancellationToken);

    [Description("Update an absence in Simplicate. Omitted values are preserved; supplied false and zero values are not omitted. Supported clients review a prefilled form; other clients update supplied values directly.")]
    [McpServerTool(Title = "Update absence in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_UpdateAbsence(
        [Description("Exact absence record ID.")] string absenceId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Employee ID.")] string? employeeId = null,
        [Description("Absence type ID.")] string? absenceTypeId = null,
        [Description("Absence start date and time, yyyy-MM-dd HH:mm:ss.")] string? startDate = null,
        [Description("Absence end date and time, yyyy-MM-dd HH:mm:ss.")] string? endDate = null,
        [Description("Absence registration year.")] int? year = null,
        [Description("Absence description.")] string? description = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/absence", absenceId, new SimplicateHoursAbsenceWrite
        {
            EmployeeId = employeeId, AbsenceTypeId = absenceTypeId, StartDate = startDate, EndDate = endDate,
            Year = year, Description = description, IsTimeDefined = isTimeDefined
        }, cancellationToken);

    [Description("Create leave in Simplicate using primitive values and exact IDs. Supported clients review a lookup-backed form; other clients submit supplied values directly.")]
    [McpServerTool(Title = "Create leave in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_CreateLeave(
        [Description("Employee ID.")] string employeeId,
        [Description("Leave type ID.")] string leaveTypeId,
        [Description("Leave start date and time, yyyy-MM-dd HH:mm:ss.")] string startDate,
        [Description("Leave end date and time, yyyy-MM-dd HH:mm:ss.")] string endDate,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Leave registration year.")] int? year = null,
        [Description("Leave description.")] string? description = null,
        [Description("Number of leave hours.")] int? hours = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/leave", null, new SimplicateHoursLeaveWrite
        {
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, StartDate = startDate, EndDate = endDate,
            Year = year, Description = description, Hours = hours, IsTimeDefined = isTimeDefined
        }, cancellationToken);

    [Description("Update leave in Simplicate. Omitted values are preserved; supplied false and zero values are not omitted. Supported clients review a prefilled form; other clients update supplied values directly.")]
    [McpServerTool(Title = "Update leave in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_UpdateLeave(
        [Description("Exact leave record ID.")] string leaveId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Employee ID.")] string? employeeId = null,
        [Description("Leave type ID.")] string? leaveTypeId = null,
        [Description("Leave start date and time, yyyy-MM-dd HH:mm:ss.")] string? startDate = null,
        [Description("Leave end date and time, yyyy-MM-dd HH:mm:ss.")] string? endDate = null,
        [Description("Leave registration year.")] int? year = null,
        [Description("Leave description.")] string? description = null,
        [Description("Number of leave hours.")] int? hours = null,
        [Description("Whether start and end times are explicitly defined.")] bool? isTimeDefined = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/leave", leaveId, new SimplicateHoursLeaveWrite
        {
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, StartDate = startDate, EndDate = endDate,
            Year = year, Description = description, Hours = hours, IsTimeDefined = isTimeDefined
        }, cancellationToken);

    [Description("Create an hour type at the documented /hours/hourstype endpoint using primitive values. Supported clients review a form; other clients submit supplied values directly.")]
    [McpServerTool(Title = "Create hour type in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_CreateHourType(
        [Description("Hour type label.")] string label,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Hour type tariff as a string.")] string? tariff = null,
        [Description("Whether the hour type is blocked.")] bool? blocked = null,
        [Description("Hour type color.")] string? color = null,
        [Description("Hour type category.")] string? type = null,
        [Description("VAT class ID.")] string? vatClassId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/hourstype", null, new SimplicateHoursTypeWrite
        {
            Label = label, Tariff = tariff, Blocked = blocked, Color = color, Type = type, VatClassId = vatClassId
        }, cancellationToken);

    [Description("Update an hour type at the documented /hours/hourstype endpoint. Omitted values are preserved; supplied false values are not omitted. Supported clients review a prefilled form; other clients update supplied values directly.")]
    [McpServerTool(Title = "Update hour type in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_UpdateHourType(
        [Description("Exact hour type record ID.")] string hourTypeId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Hour type label.")] string? label = null,
        [Description("Hour type tariff as a string.")] string? tariff = null,
        [Description("Whether the hour type is blocked.")] bool? blocked = null,
        [Description("Hour type color.")] string? color = null,
        [Description("Hour type category.")] string? type = null,
        [Description("VAT class ID.")] string? vatClassId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/hourstype", hourTypeId, new SimplicateHoursTypeWrite
        {
            Label = label, Tariff = tariff, Blocked = blocked, Color = color, Type = type, VatClassId = vatClassId
        }, cancellationToken);

    [Description("Create a timesheet row in Simplicate using primitive values and exact IDs. Supported clients review a lookup-backed form; other clients submit supplied values directly.")]
    [McpServerTool(Title = "Create timesheet row in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult?> SimplicateHours_CreateTimesheetRow(
        [Description("Employee ID.")] string employeeId,
        [Description("Timesheet row start date, yyyy-MM-dd.")] string startDate,
        [Description("Timesheet row end date, yyyy-MM-dd.")] string endDate,
        [Description("Project ID.")] string projectId,
        [Description("Project service ID.")] string projectServiceId,
        [Description("Timesheet item type ID.")] string itemTypeId,
        [Description("Timesheet row type.")] string type,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/hours/timesheetrow", null, new SimplicateTimesheetRowWrite
        {
            EmployeeId = employeeId, StartDate = startDate, EndDate = endDate, ProjectId = projectId,
            ProjectServiceId = projectServiceId, ItemTypeId = itemTypeId, Type = type
        }, cancellationToken);

    [Description("Delete a timesheet row using its exact record ID, not an employee or project ID. Form-capable clients must confirm its ID; clients without form support delete directly using the supplied ID.")]
    [McpServerTool(Title = "Delete timesheet row in Simplicate", OpenWorld = false, Destructive = true)]
    public static Task<CallToolResult> SimplicateHours_DeleteTimesheetRow(
        [Description("Exact timesheet row record ID.")] string timesheetRowId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => DeleteAsync(serviceProvider, requestContext, "/hours/timesheetrow", timesheetRowId, "Timesheet row deleted.", cancellationToken);
}
