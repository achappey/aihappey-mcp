using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Simplicate.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Workflows;

public static partial class SimplicateWorkflows
{
    [Description("Create a workflow using primitive fields and complete Simplicate IDs, not names. Reviews a form when supported; otherwise submits directly. Attachment and comment mutations are not supported.")]
    [McpServerTool(Title = "Create workflow in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateWorkflows_CreateWorkflow(
        [Description("Nonempty workflow title.")] string title,
        [Description("Nonempty description. Simplicate strips disallowed HTML.")] string description,
        [Description("Complete template ID from /workflows/defaultworkflow.")] string defaultWorkflowId,
        [Description("Complete ID of the active, unblocked employee receiving the first task. A fixed template destination takes precedence.")] string destinationEmployeeId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Current-step deadline, yyyy-MM-dd; not before today or after the workflow deadline.")] string? deadlineStep = null,
        [Description("Workflow deadline, yyyy-MM-dd; not before today or the step deadline.")] string? deadlineWorkflow = null,
        [Description("Linked sales ID.")] string? salesId = null,
        [Description("Linked project ID.")] string? projectId = null,
        [Description("Linked employee ID (not the task recipient).")] string? employeeId = null,
        [Description("Linked invoice ID.")] string? invoiceId = null,
        [Description("Linked person ID.")] string? personId = null,
        [Description("Linked organization ID.")] string? organizationId = null,
        CancellationToken cancellationToken = default)
        => CreateWorkflowAsync(serviceProvider, requestContext, new SimplicateWorkflowCreate
        {
            Title = title, Description = description, DefaultWorkflowId = defaultWorkflowId,
            DestinationEmployeeId = destinationEmployeeId, DeadlineStep = deadlineStep,
            DeadlineWorkflow = deadlineWorkflow, SalesId = salesId, ProjectId = projectId,
            EmployeeId = employeeId, InvoiceId = invoiceId, PersonId = personId, OrganizationId = organizationId
        }, cancellationToken);

    [Description("Update workflow title, description or deadlines. Omitted values are preserved. Reviews a prefilled form when supported; otherwise submits only supplied fields. Use separate tools for task actions and transfers.")]
    [McpServerTool(Title = "Update workflow in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateWorkflows_UpdateWorkflow(
        [Description("Complete workflow ID returned by the API.")] string workflowId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Nonempty workflow title; omit to preserve.")] string? title = null,
        [Description("Nonempty description; omit to preserve. Simplicate strips disallowed HTML.")] string? description = null,
        [Description("Step deadline, yyyy-MM-dd; not before today or after the effective workflow deadline.")] string? deadlineStep = null,
        [Description("Workflow deadline, yyyy-MM-dd; not before today or the effective step deadline.")] string? deadlineWorkflow = null,
        CancellationToken cancellationToken = default)
        => UpdateWorkflowAsync(serviceProvider, requestContext, workflowId, new SimplicateWorkflowEdit
        {
            Title = title, Description = description, DeadlineStep = deadlineStep, DeadlineWorkflow = deadlineWorkflow
        }, cancellationToken);

    [Description("Complete the current workflow task by executing one of its current actions[].id values, never a task ID. Advances to the follow-up task or finishes the workflow. Reviews a form when supported; otherwise executes directly.")]
    [McpServerTool(Title = "Execute workflow action in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateWorkflows_ExecuteWorkflowAction(
        [Description("Complete workflow ID.")] string workflowId,
        [Description("Current actions[].id from this workflow (defaultaction:...), NOT a task ID.")] string actionId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Reaction to log; required when the action has is_response_required.")] string? reaction = null,
        [Description("Next-task employee ID. Omit to use task destination settings. Required if those settings resolve to nobody; fixed destinations restrict this choice.")] string? nextEmployeeId = null,
        CancellationToken cancellationToken = default)
        => ExecuteActionAsync(serviceProvider, requestContext, workflowId, new SimplicateWorkflowAction
        {
            ActionId = actionId, Reaction = reaction, NextEmployeeId = nextEmployeeId
        }, cancellationToken);

    [Description("Transfer a workflow to another employee when its current task allows transfer. Reviews a form when supported; otherwise transfers directly using the supplied fields.")]
    [McpServerTool(Title = "Transfer workflow in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateWorkflows_TransferWorkflow(
        [Description("Complete workflow ID.")] string workflowId,
        [Description("Complete target employee ID.")] string transferToEmployeeId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Message included with the transfer.")] string? transferToMessage = null,
        CancellationToken cancellationToken = default)
        => TransferWorkflowAsync(serviceProvider, requestContext, workflowId, new SimplicateWorkflowTransfer
        {
            TransferToEmployeeId = transferToEmployeeId, TransferToMessage = transferToMessage
        }, cancellationToken);

    [Description("Delete a workflow. Type its exact ID to confirm when form elicitation is supported; otherwise deletes directly. Declined or cancelled confirmations never delete.")]
    [McpServerTool(Title = "Delete workflow in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateWorkflows_DeleteWorkflow(
        [Description("Complete workflow ID to delete.")] string workflowId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = WorkflowPath(workflowId);
        if (requestContext.SupportsFormElicitation() || HasResponse<ConfirmDeleteSimplicateWorkflow>(requestContext))
        {
            var confirmation = requestContext.Elicit(new ConfirmDeleteSimplicateWorkflow(), message: workflowId);
            if (!string.Equals(confirmation.Name?.Trim(), workflowId, StringComparison.Ordinal))
                return "Confirmation must match the exact workflow ID.".ToErrorCallToolResponse();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return await serviceProvider.DeleteSimplicateResourceAsync(path, $"Workflow '{workflowId}' deleted.", cancellationToken);
    }
}
