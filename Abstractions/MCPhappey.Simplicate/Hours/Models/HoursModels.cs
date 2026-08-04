using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
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

[Description("Please fill in the hour approval details")]
public sealed class SimplicateHourApprovalWriteModel
{
    [JsonPropertyName("date")]
    [Required]
    [Description("Approval date in yyyy-MM-dd format.")]
    public string? Date { get; set; }

    [JsonPropertyName("employee_id")]
    [Required]
    [Description("Employee id. The displayed option is the employee name, while the submitted value is the Simplicate employee id.")]
    public string? EmployeeId { get; set; }

    [JsonPropertyName("approvalstatus_id")]
    [Required]
    [Description("Approval status id.")]
    public string? ApprovalStatusId { get; set; }
}

public sealed class SimplicateHourApproval
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("employee")]
    public SimplicateHourApprovalReference? Employee { get; set; }

    [JsonPropertyName("employee_id")]
    public string? EmployeeId { get; set; }

    [JsonPropertyName("approvalstatus")]
    public SimplicateHourApprovalReference? ApprovalStatus { get; set; }

    [JsonPropertyName("approvalstatus_id")]
    public string? ApprovalStatusId { get; set; }
}

public sealed class SimplicateHourApprovalReference
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

[Description("Please confirm deletion of the Simplicate hour approval id: {0}")]
public sealed class ConfirmDeleteSimplicateHourApproval : IHasName
{
    [Description("Hour approval id")]
    public string Name { get; set; } = string.Empty;
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
