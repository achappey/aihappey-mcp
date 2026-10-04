using System.ComponentModel;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Simplicate.Hours.Models;

[Description("Review the hour registration details. Omitted fields are preserved on updates.")]
public sealed class SimplicateHourWrite
{
    [JsonPropertyName("source"), Description("Source of the hour registration.")]
    public string? Source { get; set; }
    [JsonPropertyName("note"), Description("Hour registration note.")]
    public string? Note { get; set; }
    [JsonPropertyName("start_date"), Description("Start date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? StartDate { get; set; }
    [JsonPropertyName("hours"), Description("Number of registered hours.")]
    public double? Hours { get; set; }
    [JsonPropertyName("duration_in_minutes"), Description("Duration in minutes.")]
    public int? DurationInMinutes { get; set; }
    [JsonPropertyName("end_date"), Description("End date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? EndDate { get; set; }
    [JsonPropertyName("is_time_defined"), Description("Whether the registration has explicitly defined start and end times.")]
    public bool? IsTimeDefined { get; set; }
    [JsonPropertyName("is_recurring"), Description("Whether the registration recurs.")]
    public bool? IsRecurring { get; set; }
    [JsonPropertyName("is_external"), Description("Whether the registration originates from an external system.")]
    public bool? IsExternal { get; set; }
    [JsonPropertyName("billable"), Description("Whether the registered hours are billable.")]
    public bool? Billable { get; set; }
    [JsonPropertyName("assignment_id"), Description("Project assignment ID.")]
    public string? AssignmentId { get; set; }
    [JsonPropertyName("should_sync_to_cronofy"), Description("Whether the registration should synchronize to Cronofy.")]
    public bool? ShouldSyncToCronofy { get; set; }
    [JsonPropertyName("external_url"), Description("URL of the related external item.")]
    public string? ExternalUrl { get; set; }
    [JsonPropertyName("employee_id"), Description("Employee ID.")]
    public string? EmployeeId { get; set; }
    [JsonPropertyName("project_id"), Description("Project ID.")]
    public string? ProjectId { get; set; }
    [JsonPropertyName("projectservice_id"), Description("Project service ID.")]
    public string? ProjectServiceId { get; set; }
    [JsonPropertyName("type_id"), Description("Hour type ID.")]
    public string? TypeId { get; set; }
    [JsonPropertyName("approvalstatus_id"), Description("Approval status ID.")]
    public string? ApprovalStatusId { get; set; }
    [JsonPropertyName("location"), Description("Registration location.")]
    public string? Location { get; set; }
    [JsonPropertyName("address_id"), Description("Existing address ID.")]
    public string? AddressId { get; set; }
    [JsonPropertyName("address_type"), Description("Address type for the nested address.")]
    public string? AddressType { get; set; }
    [JsonPropertyName("address_line_1"), Description("First address line.")]
    public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2"), Description("Second address line.")]
    public string? AddressLine2 { get; set; }
    [JsonPropertyName("address_postal_code"), Description("Address postal code.")]
    public string? AddressPostalCode { get; set; }
    [JsonPropertyName("address_province"), Description("Address province or region.")]
    public string? AddressProvince { get; set; }
    [JsonPropertyName("address_locality"), Description("Address city or locality.")]
    public string? AddressLocality { get; set; }
    [JsonPropertyName("address_country_code"), Description("Address country code.")]
    public string? AddressCountryCode { get; set; }
    [JsonPropertyName("address_country_id"), Description("Address country ID.")]
    public string? AddressCountryId { get; set; }
    [JsonPropertyName("clear_address"), Description("True explicitly clears the existing nested address; omit or use false to avoid clearing it.")]
    public bool? ClearAddress { get; set; }
    [JsonPropertyName("recurrence_id"), Description("Recurrence ID.")]
    public string? RecurrenceId { get; set; }
    [JsonPropertyName("recurrence_update"), Description("Recurrence update scope or mode.")]
    public string? RecurrenceUpdate { get; set; }
    [JsonPropertyName("recurrence_rrule_dtstart"), Description("Recurrence rule start date and time.")]
    public string? RecurrenceRruleDtstart { get; set; }
    [JsonPropertyName("recurrence_rrule_freq"), Description("Recurrence rule frequency as an integer.")]
    public int? RecurrenceRruleFreq { get; set; }
    [JsonPropertyName("recurrence_rrule_until"), Description("Recurrence rule end date and time.")]
    public string? RecurrenceRruleUntil { get; set; }
    [JsonPropertyName("recurrence_rrule_count"), Description("Number of recurrence occurrences.")]
    public int? RecurrenceRruleCount { get; set; }
    [JsonPropertyName("recurrence_rrule_interval"), Description("Interval between recurrence occurrences.")]
    public int? RecurrenceRruleInterval { get; set; }
    [JsonPropertyName("recurrence_rrule_wkst"), Description("Recurrence rule week-start day.")]
    public string? RecurrenceRruleWkst { get; set; }
    [JsonPropertyName("recurrence_rrule_byday"), Description("Recurrence rule day selection as a scalar string, not JSON.")]
    public string? RecurrenceRruleByday { get; set; }
    [JsonPropertyName("recurrence_rrule_bysetpos"), Description("Recurrence rule set-position selection as a scalar string, not JSON.")]
    public string? RecurrenceRruleBysetpos { get; set; }
    [JsonPropertyName("external_item_id"), Description("Arbitrary external item UID; this is not a Simplicate record ID.")]
    public string? ExternalItemId { get; set; }
}

[Description("Review the employee and date range for hours submission.")]
public sealed class SimplicateHoursSubmissionWrite
{
    [JsonPropertyName("employee_id"), Description("Employee ID.")]
    public string? EmployeeId { get; set; }
    [JsonPropertyName("start_date"), Description("Submission start date, yyyy-MM-dd.")]
    public string? StartDate { get; set; }
    [JsonPropertyName("end_date"), Description("Submission end date, yyyy-MM-dd.")]
    public string? EndDate { get; set; }
}

[Description("Review the absence details. Omitted fields are preserved on updates.")]
public sealed class SimplicateHoursAbsenceWrite
{
    [JsonPropertyName("start_date"), Description("Absence start date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? StartDate { get; set; }
    [JsonPropertyName("end_date"), Description("Absence end date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? EndDate { get; set; }
    [JsonPropertyName("year"), Description("Absence registration year.")]
    public int? Year { get; set; }
    [JsonPropertyName("description"), Description("Absence description.")]
    public string? Description { get; set; }
    [JsonPropertyName("employee_id"), Description("Employee ID.")]
    public string? EmployeeId { get; set; }
    [JsonPropertyName("absence_type_id"), Description("Absence type ID.")]
    public string? AbsenceTypeId { get; set; }
    [JsonPropertyName("is_time_defined"), Description("Whether the absence has explicitly defined start and end times.")]
    public bool? IsTimeDefined { get; set; }
}

[Description("Review the leave details. Omitted fields are preserved on updates.")]
public sealed class SimplicateHoursLeaveWrite
{
    [JsonPropertyName("start_date"), Description("Leave start date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? StartDate { get; set; }
    [JsonPropertyName("end_date"), Description("Leave end date and time, yyyy-MM-dd HH:mm:ss.")]
    public string? EndDate { get; set; }
    [JsonPropertyName("year"), Description("Leave registration year.")]
    public int? Year { get; set; }
    [JsonPropertyName("description"), Description("Leave description.")]
    public string? Description { get; set; }
    [JsonPropertyName("employee_id"), Description("Employee ID.")]
    public string? EmployeeId { get; set; }
    [JsonPropertyName("leave_type_id"), Description("Leave type ID.")]
    public string? LeaveTypeId { get; set; }
    [JsonPropertyName("hours"), Description("Number of leave hours.")]
    public int? Hours { get; set; }
    [JsonPropertyName("is_time_defined"), Description("Whether the leave has explicitly defined start and end times.")]
    public bool? IsTimeDefined { get; set; }
}

[Description("Review the hour type details. Omitted fields are preserved on updates.")]
public sealed class SimplicateHoursTypeWrite
{
    [JsonPropertyName("label"), Description("Hour type label.")]
    public string? Label { get; set; }
    [JsonPropertyName("tariff"), Description("Hour type tariff as a string.")]
    public string? Tariff { get; set; }
    [JsonPropertyName("blocked"), Description("Whether the hour type is blocked.")]
    public bool? Blocked { get; set; }
    [JsonPropertyName("color"), Description("Hour type color.")]
    public string? Color { get; set; }
    [JsonPropertyName("type"), Description("Hour type category.")]
    public string? Type { get; set; }
    [JsonPropertyName("vatclass_id"), Description("VAT class ID.")]
    public string? VatClassId { get; set; }
}

[Description("Review the timesheet row details.")]
public sealed class SimplicateTimesheetRowWrite
{
    [JsonPropertyName("employee_id"), Description("Employee ID.")]
    public string? EmployeeId { get; set; }
    [JsonPropertyName("start_date"), Description("Timesheet row start date, yyyy-MM-dd.")]
    public string? StartDate { get; set; }
    [JsonPropertyName("end_date"), Description("Timesheet row end date, yyyy-MM-dd.")]
    public string? EndDate { get; set; }
    [JsonPropertyName("project_id"), Description("Project ID.")]
    public string? ProjectId { get; set; }
    [JsonPropertyName("project_service_id"), Description("Project service ID.")]
    public string? ProjectServiceId { get; set; }
    [JsonPropertyName("itemtype_id"), Description("Timesheet item type ID.")]
    public string? ItemTypeId { get; set; }
    [JsonPropertyName("type"), Description("Timesheet row type.")]
    public string? Type { get; set; }
}

[Description("Type {0} to confirm deletion of the Simplicate hours record.")]
public sealed class ConfirmDeleteSimplicateHoursRecord : IHasName
{
    [Description("Type the exact record ID shown in the confirmation message.")]
    public string Name { get; set; } = string.Empty;
}
