using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;

namespace MCPhappey.Simplicate.Projects;

public static partial class SimplicateProjects
{
    [Description("Review the project details. Omitted fields are preserved on updates.")]
    public class SimplicateNewProject
    {
        [JsonPropertyName("name"), Description("Project name.")]
        public string? Name { get; set; }
        [JsonPropertyName("project_manager_id"), Description("Project manager employee ID.")]
        public string? ProjectManagerId { get; set; }
        [JsonPropertyName("my_organization_profile_id"), Description("Own organization profile ID.")]
        public string? MyOrganizationProfileId { get; set; }
        [JsonPropertyName("organization_id"), Description("Customer organization ID.")]
        public string? OrganizationId { get; set; }
        [JsonPropertyName("person_id"), Description("Customer person ID.")]
        public string? PersonId { get; set; }
        [JsonPropertyName("contact_id"), Description("Contact-person link ID, not a person ID.")]
        public string? ContactId { get; set; }
        [JsonPropertyName("billable"), Description("Whether the project is billable.")]
        public bool? Billable { get; set; }
        [JsonPropertyName("can_register_mileage"), Description("Allow mileage registration.")]
        public bool? CanRegisterMileage { get; set; }
        [JsonPropertyName("project_number"), Description("Project number, not the record ID.")]
        public string? ProjectNumber { get; set; }
        [JsonPropertyName("note"), Description("Project note.")]
        public string? Note { get; set; }
        [JsonPropertyName("start_date"), Description("Start date, yyyy-MM-dd.")]
        public string? StartDate { get; set; }
        [JsonPropertyName("end_date"), Description("End date, yyyy-MM-dd.")]
        public string? EndDate { get; set; }
        [JsonPropertyName("invoice_reference"), Description("Invoice reference.")]
        public string? InvoiceReference { get; set; }
        [JsonPropertyName("project_status_id"), Description("Project status ID.")]
        public string? ProjectStatusId { get; set; }
        [JsonPropertyName("divergent_payment_term_id"), Description("Payment term ID, or literal string 'null' to unset.")]
        public string? DivergentPaymentTermId { get; set; }
        [JsonPropertyName("invoice_recipient_is_separate"), Description("Use a separate invoice recipient.")]
        public bool? InvoiceRecipientIsSeparate { get; set; }
        [JsonPropertyName("invoice_recipient_organization_id"), Description("Separate invoice-recipient organization ID.")]
        public string? InvoiceRecipientOrganizationId { get; set; }
        [JsonPropertyName("invoice_recipient_person_id"), Description("Separate invoice-recipient person ID.")]
        public string? InvoiceRecipientPersonId { get; set; }
        [JsonPropertyName("invoice_recipient_contact_id"), Description("Separate invoice-recipient contact-person link ID.")]
        public string? InvoiceRecipientContactId { get; set; }
        [JsonPropertyName("team_ids"), Description("Comma-separated team IDs. Omit to preserve; empty removes known current assignments. If current teams are absent from GET, empty preserves them; use the single-team tool for explicit removal.")]
        public string? TeamIds { get; set; }
    }

    [Description("Review the project service details.")]
    public class SimplicateProjectServiceWrite
    {
        [JsonPropertyName("name"), Description("Service name.")]
        public string? Name { get; set; }
        [JsonPropertyName("default_service_id"), Description("Default-service ID.")]
        public string? DefaultServiceId { get; set; }
        [JsonPropertyName("explanation"), Description("Service explanation.")]
        public string? Explanation { get; set; }
        [JsonPropertyName("invoice_method"), Description("Only FixedFee is documented for writes. Existing other methods are preserved unless changed.")]
        public string? InvoiceMethod { get; set; }
        [JsonPropertyName("amount"), Description("Service quantity.")]
        public decimal? Amount { get; set; }
        [JsonPropertyName("price"), Description("Unit price.")]
        public decimal? Price { get; set; }
        [JsonPropertyName("track_hours"), Description("Track hours.")]
        public bool? TrackHours { get; set; }
        [JsonPropertyName("track_cost"), Description("Track costs.")]
        public bool? TrackCost { get; set; }
        [JsonPropertyName("vat_class_id"), Description("VAT class ID.")]
        public string? VatClassId { get; set; }
        [JsonPropertyName("revenue_group_id"), Description("Revenue group ID.")]
        public string? RevenueGroupId { get; set; }
    }

    [Description("Review the new project service details.")]
    public class SimplicateNewProjectService : SimplicateProjectServiceWrite
    {
        [JsonPropertyName("project_id"), Description("Project ID containing the service.")]
        public string? ProjectId { get; set; }
        [JsonPropertyName("external_id"), Description("External service identifier.")]
        public string? ExternalId { get; set; }
        [JsonPropertyName("related_service_id"), Description("Related service ID.")]
        public string? RelatedServiceId { get; set; }
        [JsonPropertyName("use_in_resource_planner"), Description("Use in the resource planner.")]
        public bool? UseInResourcePlanner { get; set; }
        [JsonPropertyName("start_date"), Description("Start date, yyyy-MM-dd.")]
        public string? StartDate { get; set; }
        [JsonPropertyName("end_date"), Description("Service end or invoice eligibility date, yyyy-MM-dd.")]
        public string? EndDate { get; set; }
        [JsonPropertyName("write_hours_start_date"), Description("First date allowing hours, yyyy-MM-dd.")]
        public string? WriteHoursStartDate { get; set; }
        [JsonPropertyName("write_hours_end_date"), Description("Last date allowing hours, yyyy-MM-dd.")]
        public string? WriteHoursEndDate { get; set; }
    }

    [Description("Review the project assignment details.")]
    public class SimplicateAssignmentWrite
    {
        [JsonPropertyName("name"), Description("Assignment name.")]
        public string? Name { get; set; }
        [JsonPropertyName("description"), Description("Assignment description.")]
        public string? Description { get; set; }
        [JsonPropertyName("use_spread"), Description("Use spread.")]
        public bool? UseSpread { get; set; }
        [JsonPropertyName("hours_type"), Description("per_week or total.")]
        public string? HoursType { get; set; }
        [JsonPropertyName("created_at"), Description("Creation timestamp, yyyy-MM-dd HH:mm:ss.")]
        public string? CreatedAt { get; set; }
        [JsonPropertyName("updated_at"), Description("Update timestamp, yyyy-MM-dd HH:mm:ss.")]
        public string? UpdatedAt { get; set; }
        [JsonPropertyName("employee_ids"), Description("Comma-separated employee IDs; omit to preserve. Empty clearing is not supported.")]
        public string? EmployeeIds { get; set; }
        [JsonPropertyName("status_id"), Description("Assignment status ID.")]
        public string? StatusId { get; set; }
    }

    [Description("Review the new project assignment details.")]
    public class SimplicateNewAssignment : SimplicateAssignmentWrite
    {
        [JsonPropertyName("start_date"), Description("Start date, yyyy-MM-dd.")]
        public string? StartDate { get; set; }
        [JsonPropertyName("end_date"), Description("End date, yyyy-MM-dd.")]
        public string? EndDate { get; set; }
        [JsonPropertyName("projecthourstype_id"), Description("Exact project-specific hour-type ID; do not substitute a general hours-type ID.")]
        public string? ProjectHoursTypeId { get; set; }
    }

    [Description("Review the employee to add to the project.")]
    public class SimplicateAddProjectEmployee
    {
        [JsonPropertyName("project_id"), Required, Description("Project ID.")]
        public string? ProjectId { get; set; }
        [JsonPropertyName("employee_id"), Required, Description("Employee ID.")]
        public string? EmployeeId { get; set; }
    }

    [Description("Review the project team assignment.")]
    public class SimplicateProjectTeamWrite
    {
        [JsonPropertyName("team_id"), Required, Description("Team ID.")]
        public string? TeamId { get; set; }
        [JsonPropertyName("assigned"), Required, Description("Assign or remove the team.")]
        public bool? Assigned { get; set; }
    }

    [Description("Type {0} to confirm deletion or removal.")]
    public class ConfirmDeleteSimplicateProjectRecord : IHasName
    {
        [Required, Description("Type the exact record ID shown in the confirmation message.")]
        public string Name { get; set; } = string.Empty;
    }
}
