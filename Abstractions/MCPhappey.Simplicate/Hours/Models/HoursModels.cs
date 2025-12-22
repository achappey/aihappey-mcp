using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Simplicate.Extensions;

namespace MCPhappey.Simplicate.Hours.Models;


public enum ApprovalStatusLabel
{
    to_approved_project,
    to_forward,
    approved,
    rejected
}

public enum InvoiceStatus
{
    invoiced
}

public class SimplicateHourTotals
{
    [JsonPropertyName("totalHours")]
    public double TotalHours { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }
}

public class SimplicateHourItem
{
    [JsonPropertyName("employee")]
    public SimplicateEmployee? Employee { get; set; }

    [JsonPropertyName("project")]
    public SimplicateProject? Project { get; set; }

    [JsonPropertyName("type")]
    public SimplicateHourType? Type { get; set; }

    [JsonPropertyName("tariff")]
    public decimal Tariff { get; set; }

    [JsonPropertyName("hours")]
    public double Hours { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonIgnore] // Don't serialize calculated property by default
    public decimal Amount
    {
        get
        {
            // Defensive: if negative hours/tariff are expected, remove checks below
            var hours = Convert.ToDecimal(Hours); // Safe: double to decimal
            var tariff = Tariff;
            // If you need to check for negative values, add:
            // if (hours < 0 || tariff < 0) return 0m;

            var amount = hours * tariff;

            // If you want to round to 2 decimals for currency (bankers rounding):
            return amount.ToAmount();
        }
    }
}

public class SimplicateEmployee
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SimplicateProject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SimplicateHourType
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

[Description("Please fill in the hour details")]
public class SimplicateNewHour
{
    [JsonPropertyName("hours")]
    [Required]
    [Description("The number of hours.")]
    public double? Hours { get; set; }

    [JsonPropertyName("employee_id")]
    [Required]
    [Description("The id of the employee.")]
    public string EmployeeId { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    [Required]
    [Description("The id of the project.")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("projectservice_id")]
    [Required]
    [Description("The id of the project service.")]
    public string ProjectServiceId { get; set; } = string.Empty;

    [JsonPropertyName("type_id")]
    [Required]
    [Description("The id of the hourtype.")]
    public string TypeId { get; set; } = string.Empty;

    [JsonPropertyName("start_date")]
    [Required]
    [Description("The start date of the hour registration.")]
    public DateTime StartDate { get; set; }

}