using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MCPhappey.Simplicate.Extensions;

namespace MCPhappey.Simplicate.Invoices.Models;

public class PaidInvoicePaymentTermSummary
{
    public int TotalInvoices { get; set; }

    public double AveragePaymentTermDays { get; set; }

    public int? MinPaymentTermDays { get; set; }

    public int? MaxPaymentTermDays { get; set; }
}

public class PaidInvoicePaymentTermDetail : SimplicateInvoice
{
    public DateTime PaymentDate { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal AmountPaid { get; set; }
}


public class SimplicateOpenInvoiceWithDaysOpen : SimplicateInvoiceTotals
{
    [JsonPropertyName("debtorName")]
    public string DebtorName { get; set; } = default!;

    // [JsonPropertyName("invoiceCount")]
    // public int InvoiceCount { get; set; }

    [JsonPropertyName("averageDaysOpen")]
    public int? AverageDaysOpen { get; set; }
}


public class SimplicateTotalInvoices : SimplicateInvoiceTotals
{
    [JsonPropertyName("month")]
    public string Month { get; set; } = default!;

}


public class SimplicateInvoiceTotals
{
    [JsonPropertyName("totalInvoices")]
    public double TotalInvoices { get; set; }

    [JsonPropertyName("totalIncludingVat")]
    public decimal TotalIncludingVat { get; set; }

    [JsonPropertyName("totalExcludingVat")]
    public decimal TotalExcludingVat { get; set; }

    [JsonPropertyName("totalOutstanding")]
    public decimal TotalOutstanding { get; set; }
}



public class SimplicateInvoice
{
    [JsonPropertyName("my_organization_profile")]
    public MyOrganizationProfile? MyOrganizationProfile { get; set; }

    [JsonPropertyName("organization")]
    public SimplicateOrganization? Organization { get; set; }

    [JsonPropertyName("payment_term")]
    public SimplicatePaymentTerm? PaymentTerm { get; set; }

    [JsonPropertyName("projects")]
    public IEnumerable<SimplicateProject>? Projects { get; set; }

    [JsonPropertyName("total_including_vat")]
    public decimal TotalIncludingVat { get; set; }

    [JsonPropertyName("total_excluding_vat")]
    public decimal TotalExcludingVat { get; set; }

    [JsonPropertyName("total_outstanding")]
    public decimal TotalOutstanding { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("days_open")]
    public int? DaysOpen
    {
        get
        {
            if (string.IsNullOrEmpty(Date)) return null;
            var now = DateTime.UtcNow;
            var dueDate = Date.ParseDate();
            return dueDate.HasValue ? (int?)(now - dueDate.Value).TotalDays : null;
        }
    }

    [JsonPropertyName("days_overdue")]
    public int? DaysOverdue
    {
        get
        {
            if (string.IsNullOrEmpty(Date)) return null;
            if (string.IsNullOrEmpty(PaymentTerm?.Days)) return null;

            var now = DateTime.UtcNow;
            var fromDate = Date.ParseDate();
            var days = PaymentTerm?.Days.ParseInt();

            if (!fromDate.HasValue || !days.HasValue) return null;
            var dueDate = fromDate.Value.AddDays(days.Value);

            return (int?)(now - dueDate).TotalDays;
        }
    }


    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; set; }
}

public class SimplicateInvoicePayment : SimplicatePayment
{
    [JsonPropertyName("invoice_number")]
    public string InvoiceNumber { get; set; } = null!;
}

public class SimplicatePayment
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("date")]
    public string Date { get; set; } = null!;

    [JsonPropertyName("invoice_id")]
    public string InvoiceId { get; set; } = null!;
}

public class MyOrganizationProfile
{
    [JsonPropertyName("organization")]
    public SimplicateOrganization? Organization { get; set; }
}

public class SimplicateOrganization
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SimplicateProjectManager
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class SimplicateProject
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("project_manager")]
    public SimplicateProjectManager? ProjectManager { get; set; }
}

public class SimplicatePaymentTerm
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("days")]
    public string Days { get; set; } = string.Empty;

}

public enum InvoiceStatusLabel
{
    [EnumMember(Value = "Payed")]
    Payed,
    [EnumMember(Value = "Sended")]
    Sended,
    [EnumMember(Value = "Expired")]
    Expired,
    [EnumMember(Value = "Concept")]
    Concept
}