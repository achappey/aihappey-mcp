using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{
    [Description("Please review the sales details. References are Simplicate IDs, not display names.")]
    public sealed class SimplicateNewSales
    {
        [JsonPropertyName("subject"), Required, Description("The sales subject.")]
        public string? Subject { get; set; }
        [JsonPropertyName("note"), Description("A note about the sales.")]
        public string? Note { get; set; }
        [JsonPropertyName("organization_id"), Description("Customer organization ID.")]
        public string? OrganizationId { get; set; }
        [JsonPropertyName("my_organization_profile_id"), Description("Own organization profile ID.")]
        public string? MyOrganizationProfileId { get; set; }
        [JsonPropertyName("person_id"), Description("Customer person ID.")]
        public string? PersonId { get; set; }
        [JsonPropertyName("contact_id"), Description("Contact-person link ID; must match the selected organization/person.")]
        public string? ContactId { get; set; }
        [JsonPropertyName("start_date"), Description("Start date, yyyy-MM-dd.")]
        public string? StartDate { get; set; }
        [JsonPropertyName("expected_closing_date"), Description("Expected closing date, yyyy-MM-dd.")]
        public string? ExpectedClosingDate { get; set; }
        [JsonPropertyName("expected_revenue"), Description("Expected revenue.")]
        public decimal? ExpectedRevenue { get; set; }
        [JsonPropertyName("chance_to_score"), Range(0, 100), Description("Chance to score, an integer percentage from 0 to 100.")]
        public int? ChanceToScore { get; set; }
        [JsonPropertyName("responsible_employee_id"), Description("Responsible employee ID.")]
        public string? ResponsibleEmployeeId { get; set; }
        [JsonPropertyName("progress_id"), Description("Sales progress ID.")]
        public string? ProgressId { get; set; }
        [JsonPropertyName("source_id"), Description("Sales source ID.")]
        public string? SourceId { get; set; }
        [JsonPropertyName("status_id"), Description("Sales status ID.")]
        public string? StatusId { get; set; }
        [JsonPropertyName("reason_id"), Description("Sales reason ID.")]
        public string? ReasonId { get; set; }
        [JsonPropertyName("lost_to_competitor_id"), Description("Competitor organization ID.")]
        public string? LostToCompetitorId { get; set; }
        [JsonPropertyName("team_ids"), Description("Comma-separated team IDs. Empty explicitly removes all existing teams; omission preserves them.")]
        public string? TeamIds { get; set; }
        [JsonPropertyName("contact_is_active"), Description("Whether the sales contact is active.")]
        public bool? ContactIsActive { get; set; }
        [JsonPropertyName("contact_work_function"), Description("Sales contact work function.")]
        public string? ContactWorkFunction { get; set; }
        [JsonPropertyName("contact_work_email"), EmailAddress, Description("Sales contact work email.")]
        public string? ContactWorkEmail { get; set; }
        [JsonPropertyName("contact_work_phone"), Description("Sales contact work phone.")]
        public string? ContactWorkPhone { get; set; }
        [JsonPropertyName("contact_work_mobile"), Description("Sales contact work mobile.")]
        public string? ContactWorkMobile { get; set; }
        [JsonPropertyName("contact_person_id"), Description("Person ID inside the sales contact object.")]
        public string? ContactPersonId { get; set; }
        [JsonPropertyName("invoice_recipient_is_separate"), Description("Use a separate invoice recipient.")]
        public bool? InvoiceRecipientIsSeparate { get; set; }
        [JsonPropertyName("invoice_recipient_organization_id"), Description("Separate invoice-recipient organization ID.")]
        public string? InvoiceRecipientOrganizationId { get; set; }
        [JsonPropertyName("invoice_recipient_person_id"), Description("Separate invoice-recipient person ID.")]
        public string? InvoiceRecipientPersonId { get; set; }
        [JsonPropertyName("invoice_recipient_contact_id"), Description("Separate invoice-recipient contact-person link ID.")]
        public string? InvoiceRecipientContactId { get; set; }
    }

    [Description("Please review the sales service details. Hour-type and cost-type collections are not edited.")]
    public sealed class SimplicateSalesServiceWrite
    {
        [JsonPropertyName("name"), Required, Description("Service name.")]
        public string? Name { get; set; }
        [JsonPropertyName("sales_id"), Required, Description("Sales ID containing the service.")]
        public string? SalesId { get; set; }
        [JsonPropertyName("default_service_id"), Description("Default-service ID.")]
        public string? DefaultServiceId { get; set; }
        [JsonPropertyName("explanation"), Description("Service explanation.")]
        public string? Explanation { get; set; }
        [JsonPropertyName("invoice_method"), Description("Supported invoice method: FixedFee.")]
        public string? InvoiceMethod { get; set; }
        [JsonPropertyName("amount"), Description("Service quantity.")]
        public decimal? Amount { get; set; }
        [JsonPropertyName("price"), Description("Service unit price.")]
        public decimal? Price { get; set; }
        [JsonPropertyName("track_hours"), Description("Track hours for this service.")]
        public bool? TrackHours { get; set; }
        [JsonPropertyName("track_cost"), Description("Track costs for this service.")]
        public bool? TrackCost { get; set; }
        [JsonPropertyName("show_itemtype"), Description("Item-type display setting (integer API value).")]
        public int? ShowItemtype { get; set; }
        [JsonPropertyName("total"), Description("Total as the string value accepted by the API.")]
        public string? Total { get; set; }
        [JsonPropertyName("total_incl_vat"), Description("Total including VAT.")]
        public decimal? TotalInclVat { get; set; }
        [JsonPropertyName("position"), Description("Service position (integer).")]
        public int? Position { get; set; }
        [JsonPropertyName("subscription_cycle"), Description("Month, Quarter, Half_a_year, or Year.")]
        public string? SubscriptionCycle { get; set; }
        [JsonPropertyName("revenue_group_id"), Description("Revenue-group ID.")]
        public string? RevenueGroupId { get; set; }
    }
}

[Description("Please confirm deletion of the Simplicate sales service ID: {0}")]
public sealed class ConfirmDeleteSimplicateSalesService : IHasName
{
    [Required, Description("Type the exact service ID to confirm deletion.")]
    public string? Name { get; set; }
}
