using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
        [Required]
        [Description("Supervisor id")]
        public string? SupervisorId { get; set; }

        [JsonPropertyName("status.id")]
        [Required]
        [Description("Status id")]
        public string? StatusId { get; set; }
    }

    public class SimplicateTimetable
    {
        [JsonPropertyName("employee")]
        public Employee Employee { get; set; } = null!;

        [JsonPropertyName("even_week")]
        public WeekSchedule EvenWeek { get; set; } = null!;

        [JsonPropertyName("odd_week")]
        public WeekSchedule OddWeek { get; set; } = null!;

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
        public DaySchedule Day1 { get; set; } = null!;

        [JsonPropertyName("day_2")]
        public DaySchedule Day2 { get; set; } = null!;

        [JsonPropertyName("day_3")]
        public DaySchedule Day3 { get; set; } = null!;

        [JsonPropertyName("day_4")]
        public DaySchedule Day4 { get; set; } = null!;

        [JsonPropertyName("day_5")]
        public DaySchedule Day5 { get; set; } = null!;

        [JsonPropertyName("day_6")]
        public DaySchedule Day6 { get; set; } = null!;

        [JsonPropertyName("day_7")]
        public DaySchedule Day7 { get; set; } = null!;
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

    public class SimplicateLeave
    {
        [JsonPropertyName("employee")]
        public Employee? Employee { get; set; }

        [JsonPropertyName("leavetype")]
        public LeaveType? LeaveType { get; set; }

        [JsonPropertyName("leave_status")]
        public LeaveStatus? LeaveStatus { get; set; }

        [JsonPropertyName("start_date")]
        public string? StartDate { get; set; }

        [JsonPropertyName("hours")]
        public double Hours { get; set; }
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

        [JsonPropertyName("employment_status")]
        public string? EmploymentStatus { get; set; }

        [JsonPropertyName("civil_status")]
        public SimplicateCivilStatus? CivilStatus { get; set; }

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
}

