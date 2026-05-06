using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate.HRM;

public static partial class SimplicateHRM
{

    [Description("Please fill in the employee details")]
    public class SimplicateNewEmployee
    {
        [JsonPropertyName("person_id")]
        [Required]
        [Description("The person id.")]
        public string PersonId { get; set; } = null!;

        [JsonPropertyName("supervisor.id")]
        [Description("Supervisor employee id.")]
        public string? SupervisorId { get; set; }

        [JsonPropertyName("status.id")]
        [Description("Employee status id.")]
        public string? StatusId { get; set; }

        [JsonPropertyName("custom_fields")]
        [Description("Custom employee fields as name and value pairs.")]
        public List<SimplicateCustomFieldValue>? CustomFields { get; set; }
    }

    [Description("Please fill in the absence details")]
    public class SimplicateNewAbsence
    {
        [JsonPropertyName("start_date")]
        [Required]
        [Description("Absence start date.")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        [Required]
        [Description("Absence end date.")]
        public string? EndDate { get; set; }

        [JsonPropertyName("year")]
        [Required]
        [Description("Calendar year the absence belongs to.")]
        public int? Year { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional description for the absence.")]
        public string? Description { get; set; }

        [JsonPropertyName("employee_id")]
        [Required]
        [Description("Employee id for the absence. The displayed option is the employee name, while the submitted value is the Simplicate employee id.")]
        public string? EmployeeId { get; set; }

        [JsonPropertyName("absence_type_id")]
        [Required]
        [Description("Absence type id. The displayed option is the absence type label, while the submitted value is the Simplicate absence type id.")]
        public string? AbsenceTypeId { get; set; }

        [JsonPropertyName("is_time_defined")]
        [Description("Whether the absence is time defined.")]
        public bool? IsTimeDefined { get; set; }
    }

    [Description("Please fill in the leave details")]
    public class SimplicateNewLeave
    {
        [JsonPropertyName("start_date")]
        [Required]
        [Description("Leave start date.")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        [Required]
        [Description("Leave end date.")]
        public string? EndDate { get; set; }

        [JsonPropertyName("year")]
        [Required]
        [Description("Calendar year the leave belongs to.")]
        public int? Year { get; set; }

        [JsonPropertyName("hours")]
        [Description("Number of leave hours.")]
        public double? Hours { get; set; }

        [JsonPropertyName("description")]
        [Description("Optional description for the leave.")]
        public string? Description { get; set; }

        [JsonPropertyName("employee_id")]
        [Required]
        [Description("Employee id for the leave. The displayed option is the employee name, while the submitted value is the Simplicate employee id.")]
        public string? EmployeeId { get; set; }

        [JsonPropertyName("leave_type_id")]
        [Required]
        [Description("Leave type id. The displayed option is the leave type label, while the submitted value is the Simplicate leave type id.")]
        public string? LeaveTypeId { get; set; }

        [JsonPropertyName("is_time_defined")]
        [Description("Whether the leave is time defined.")]
        public bool? IsTimeDefined { get; set; }
    }

    [Description("Please fill in the timetable details")]
    public class SimplicateNewTimetable
    {
        [JsonPropertyName("even_week")]
        [Description("Timetable definition for even weeks.")]
        public WeekSchedule EvenWeek { get; set; } = new();

        [JsonPropertyName("odd_week")]
        [Description("Timetable definition for odd weeks.")]
        public WeekSchedule OddWeek { get; set; } = new();

        [JsonPropertyName("start_date")]
        [Required]
        [Description("Timetable start date.")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        [Description("Timetable end date.")]
        public string? EndDate { get; set; }

        [JsonPropertyName("productivity_target")]
        [Description("Productivity target percentage or value.")]
        public int? ProductivityTarget { get; set; }

        [JsonPropertyName("should_write_hours")]
        [Description("Whether the employee should write hours.")]
        public bool? ShouldWriteHours { get; set; }

        [JsonPropertyName("employee_id")]
        [Required]
        [Description("Employee id for the timetable. The displayed option is the employee name, while the submitted value is the Simplicate employee id.")]
        public string? EmployeeId { get; set; }

        [JsonPropertyName("has_odd_weeks")]
        [Description("Whether the timetable has a separate odd week schedule.")]
        public bool? HasOddWeeks { get; set; }
    }

    public class SimplicateTimetable
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("employee")]
        public Employee? Employee { get; set; }

        [JsonPropertyName("even_week")]
        public WeekSchedule EvenWeek { get; set; } = new();

        [JsonPropertyName("odd_week")]
        public WeekSchedule OddWeek { get; set; } = new();

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("productivity_target")]
        public int? ProductivityTarget { get; set; }

        [JsonPropertyName("should_write_hours")]
        public bool? ShouldWriteHours { get; set; }

        [JsonPropertyName("has_odd_weeks")]
        public bool? HasOddWeeks { get; set; }

        [JsonIgnore]
        public double AverageWorkdayHours
        {
            get
            {
                var allDays = EvenWeek.AllDays.Concat(OddWeek.AllDays).ToList();
                var workedDays = allDays.Where(d => d.Hours > 0).ToList();
                if (workedDays.Count == 0) return 0;
                return workedDays.Sum(d => d.Hours) / workedDays.Count;
            }
        }
    }

    public class WeekSchedule
    {
        [JsonIgnore]
        public DaySchedule[] AllDays =>
            [Day1, Day2, Day3, Day4, Day5, Day6, Day7];

        [JsonPropertyName("day_1")]
        public DaySchedule Day1 { get; set; } = new();

        [JsonPropertyName("day_2")]
        public DaySchedule Day2 { get; set; } = new();

        [JsonPropertyName("day_3")]
        public DaySchedule Day3 { get; set; } = new();

        [JsonPropertyName("day_4")]
        public DaySchedule Day4 { get; set; } = new();

        [JsonPropertyName("day_5")]
        public DaySchedule Day5 { get; set; } = new();

        [JsonPropertyName("day_6")]
        public DaySchedule Day6 { get; set; } = new();

        [JsonPropertyName("day_7")]
        public DaySchedule Day7 { get; set; } = new();
    }

    public class DaySchedule
    {
        [JsonPropertyName("start_time")]
        public double StartTime { get; set; }

        [JsonPropertyName("end_time")]
        public double EndTime { get; set; }

        [JsonPropertyName("hours")]
        public double Hours { get; set; }
    }

    public class SimplicateAbsence
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("employee")]
        public Employee? Employee { get; set; }

        [JsonPropertyName("absence_type")]
        public SimplicateAbsenceType? AbsenceType { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("is_time_defined")]
        public bool? IsTimeDefined { get; set; }
    }

    public class SimplicateLeave
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("employee")]
        public Employee? Employee { get; set; }

        [JsonPropertyName("leavetype")]
        public LeaveType? LeaveType { get; set; }

        [JsonPropertyName("leave_status")]
        public LeaveStatus? LeaveStatus { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("hours")]
        public double Hours { get; set; }

        [JsonPropertyName("is_time_defined")]
        public bool? IsTimeDefined { get; set; }
    }

    public class SimplicateIdItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    public class Employee
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    public class LeaveType
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class SimplicateAbsenceType
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class LeaveStatus
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class LeaveTotals
    {
        [JsonPropertyName("leaveType")]
        public string LeaveType { get; set; } = string.Empty;

        [JsonPropertyName("totalDays")]
        public double TotalDays { get; set; }

        [JsonPropertyName("totalHours")]
        public double TotalHours { get; set; }

        [JsonPropertyName("totalHoursPlanned")]
        public double TotalHoursPlanned { get; set; }
    }

    public class SimplicateEmployee
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("person_id")]
        public string PersonId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("bank_account")]
        public string? BankAccount { get; set; }

        [JsonPropertyName("function")]
        public string? Function { get; set; }

        [JsonPropertyName("type")]
        public SimplicateEmployeeType? Type { get; set; }

        [JsonPropertyName("status")]
        public SimplicateEmployeeStatus? Status { get; set; }

        [JsonPropertyName("supervisor")]
        public Employee? Supervisor { get; set; }

        [JsonPropertyName("employment_status")]
        public string? EmploymentStatus { get; set; }

        [JsonPropertyName("civil_status")]
        public SimplicateCivilStatus? CivilStatus { get; set; }

        [JsonPropertyName("custom_fields")]
        public List<SimplicateCustomFieldValue>? CustomFields { get; set; }

        [JsonPropertyName("work_phone")]
        public string? WorkPhone { get; set; }

        [JsonPropertyName("work_mobile")]
        public string? WorkMobile { get; set; }

        [JsonPropertyName("work_email")]
        public string? WorkEmail { get; set; }

        [JsonPropertyName("hourly_sales_tariff")]
        public string? HourlySalesTariff { get; set; }

        [JsonPropertyName("hourly_cost_tariff")]
        public string? HourlyCostTariff { get; set; }

        [JsonPropertyName("avatar")]
        public SimplicateAvatar? Avatar { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("simplicate_url")]
        public string? SimplicateUrl { get; set; }
    }

    public class SimplicateEmployeeStatus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class SimplicateCustomFieldValue
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    public class SimplicateEmployeeType
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class SimplicateCivilStatus
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    public class SimplicateAvatar
    {
        [JsonPropertyName("url_small")]
        public string? UrlSmall { get; set; }

        [JsonPropertyName("url_large")]
        public string? UrlLarge { get; set; }

        [JsonPropertyName("initials")]
        public string? Initials { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }
    }

    private sealed record LeaveRow(
        SimplicateLeaveRecord Source,
        int Year,
        DateOnly? StartDate,
        DateOnly? EndDate,
        bool AffectsBalance);

    public sealed class SimplicateLeaveRecord
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string? EndDate { get; set; }

        [JsonPropertyName("year")]
        [JsonConverter(typeof(FlexibleNullableIntConverter))]
        public int? Year { get; set; }

        [JsonPropertyName("hours")]
        public double Hours { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("employee")]
        public SimplicateEmployeeRef? Employee { get; set; }

        [JsonPropertyName("leavetype")]
        public SimplicateLeaveTypeRef? LeaveType { get; set; }

        [JsonPropertyName("leave_status")]
        public SimplicateLeaveStatusRef? LeaveStatus { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class SimplicateEmployeeRef
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("person_id")]
        public string? PersonId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class SimplicateLeaveTypeRef
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("blocked")]
        public bool? Blocked { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("affects_balance")]
        public bool? AffectsBalance { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class SimplicateLeaveStatusRef
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    private sealed class FlexibleNullableIntConverter : JsonConverter<int?>
    {
        public override int? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out var intValue))
                    return intValue;

                if (reader.TryGetDouble(out var doubleValue))
                    return (int)Math.Truncate(doubleValue);

                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();

                if (string.IsNullOrWhiteSpace(value))
                    return null;

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    return intValue;

                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                    return (int)Math.Truncate(doubleValue);

                return null;
            }

            reader.Skip();
            return null;
        }

        public override void Write(
            Utf8JsonWriter writer,
            int? value,
            JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteNumberValue(value.Value);
            else
                writer.WriteNullValue();
        }
    }

}

