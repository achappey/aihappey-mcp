using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate.Workflows;

public static partial class SimplicateWorkflows
{
    [Description("Review workflow title, description and deadlines. Unchanged fields are preserved.")]
    public class SimplicateWorkflowEdit
    {
        [JsonPropertyName("title"), Description("Nonempty workflow title.")]
        public string? Title { get; set; }
        [JsonPropertyName("description"), Description("Nonempty workflow description. Disallowed HTML is stripped by Simplicate.")]
        public string? Description { get; set; }
        [JsonPropertyName("deadline_step"), Description("Step deadline, yyyy-MM-dd; at least today and at most the workflow deadline.")]
        public string? DeadlineStep { get; set; }
        [JsonPropertyName("deadline_workflow"), Description("Workflow deadline, yyyy-MM-dd; at least today and the step deadline.")]
        public string? DeadlineWorkflow { get; set; }
    }

    [Description("Review the new workflow. References must be complete Simplicate IDs, not display names.")]
    public sealed class SimplicateWorkflowCreate : SimplicateWorkflowEdit
    {
        [JsonPropertyName("defaultworkflow_id"), Required, Description("Workflow template ID from /workflows/defaultworkflow.")]
        public string? DefaultWorkflowId { get; set; }
        [JsonPropertyName("destination_employee_id"), Required, Description("Active, unblocked first-task recipient. A fixed template destination takes precedence.")]
        public string? DestinationEmployeeId { get; set; }
        [JsonPropertyName("sales_id"), Description("Linked sales ID.")]
        public string? SalesId { get; set; }
        [JsonPropertyName("project_id"), Description("Linked project ID.")]
        public string? ProjectId { get; set; }
        [JsonPropertyName("employee_id"), Description("Linked employee ID, not the recipient.")]
        public string? EmployeeId { get; set; }
        [JsonPropertyName("invoice_id"), Description("Linked invoice ID.")]
        public string? InvoiceId { get; set; }
        [JsonPropertyName("person_id"), Description("Linked person ID.")]
        public string? PersonId { get; set; }
        [JsonPropertyName("organization_id"), Description("Linked organization ID.")]
        public string? OrganizationId { get; set; }
    }

    [Description("Review the action for the workflow's current task. This can advance or finish the workflow.")]
    public sealed class SimplicateWorkflowAction
    {
        [JsonPropertyName("id"), Required, Description("Action ID from current workflow actions[].id, never a task ID.")]
        public string? ActionId { get; set; }
        [JsonPropertyName("reaction"), Description("Reaction; required when the selected action requires a response.")]
        public string? Reaction { get; set; }
        [JsonPropertyName("employee_id"), Description("Optional next recipient ID, subject to the destination task's settings.")]
        public string? NextEmployeeId { get; set; }
    }

    [Description("Review the employee and message for transferring this workflow.")]
    public sealed class SimplicateWorkflowTransfer
    {
        [JsonPropertyName("transfer_to_employee_id"), Required, Description("Target employee ID.")]
        public string? TransferToEmployeeId { get; set; }
        [JsonPropertyName("transfer_to_message"), Description("Message to include with the transfer.")]
        public string? TransferToMessage { get; set; }
    }

    [Description("Confirm deletion of workflow ID: {0}")]
    public sealed class ConfirmDeleteSimplicateWorkflow
    {
        [Required, Description("Type the exact workflow ID to confirm deletion.")]
        public string? Name { get; set; }
    }
}
