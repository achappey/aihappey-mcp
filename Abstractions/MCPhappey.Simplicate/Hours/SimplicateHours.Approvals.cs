using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Hours.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Hours;

public static partial class SimplicateHours
{
    [Description("Create a new hour approval in Simplicate. The employee and approval status are confirmed through elicitation before creation.")]
    [McpServerTool(
        Title = "Create hour approval in Simplicate",
        Name = "simplicate_hours_create_hour_approval",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHours_CreateHourApproval(
        [Description("Approval date in yyyy-MM-dd format.")] string date,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Employee id to preselect in elicitation.")] string? employeeId = null,
        [Description("Approval status id to preselect in elicitation. Use the literal string 'null' for no approval status.")] string? approvalStatusId = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PostSimplicateResourceAsync(
            requestContext,
            "/hours/approval",
            new SimplicateHourApprovalWriteModel
            {
                Date = date,
                EmployeeId = employeeId,
                ApprovalStatusId = approvalStatusId
            },
            MapHourApprovalToWriteBody,
            GetHourApprovalWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Update an existing hour approval in Simplicate. Existing values are prefilled and the final values are confirmed through elicitation before updating.")]
    [McpServerTool(
        Title = "Update hour approval in Simplicate",
        Name = "simplicate_hours_update_hour_approval",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHours_UpdateHourApproval(
        [Description("The Simplicate hour approval id.")] string approvalId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Approval date in yyyy-MM-dd format. Existing value is used when omitted.")] string? date = null,
        [Description("Employee id to preselect in elicitation. Existing value is used when omitted.")] string? employeeId = null,
        [Description("Approval status id to preselect in elicitation. Use the literal string 'null' to clear the status.")] string? approvalStatusId = null,
        CancellationToken cancellationToken = default)
        => await serviceProvider.PutSimplicateResourceMergedAsync<SimplicateHourApproval, SimplicateHourApprovalWriteModel>(
            requestContext,
            "/hours/approval/" + approvalId,
            new SimplicateHourApprovalWriteModel
            {
                Date = date,
                EmployeeId = employeeId,
                ApprovalStatusId = approvalStatusId
            },
            (_, dto) => MapHourApprovalToWriteBody(dto),
            MapHourApprovalToWriteModel,
            GetHourApprovalWriteElicitOverridesAsync,
            cancellationToken);

    [Description("Delete an hour approval in Simplicate after typed confirmation of the exact approval id.")]
    [McpServerTool(
        Title = "Delete hour approval in Simplicate",
        Name = "simplicate_hours_delete_hour_approval",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateHours_DeleteHourApproval(
        [Description("The Simplicate hour approval id.")] string approvalId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateHourApproval>(
            expectedName: approvalId,
            async ct => await serviceProvider.DeleteSimplicateResourceAsync(
                "/hours/approval/" + approvalId,
                $"Hour approval '{approvalId}' deleted.",
                ct),
            $"Hour approval '{approvalId}' deleted.",
            cancellationToken);

    private static async Task<IReadOnlyDictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>?>
        GetHourApprovalWriteElicitOverridesAsync(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,
            SimplicateHourApprovalWriteModel dto,
            CancellationToken cancellationToken)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.OrdinalIgnoreCase);

        var employeeOverrides = await serviceProvider.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateHourApprovalWriteModel>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateHourApprovalWriteModel.EmployeeId),
                    Title = "Employee",
                    Description = "Employee for this hour approval.",
                    DefaultValue = dto.EmployeeId
                }
            ],
            cancellationToken);

        foreach (var item in employeeOverrides)
            overrides[item.Key] = item.Value;

        var approvalStatusOverrides = await serviceProvider.BuildSimplicateApprovalStatusElicitOverridesAsync<SimplicateHourApprovalWriteModel>(
            requestContext,
            [
                new SimplicateElicitFieldOverride
                {
                    PropertyName = nameof(SimplicateHourApprovalWriteModel.ApprovalStatusId),
                    Title = "Approval status",
                    Description = "Approval status for this hour approval. Select 'No approval status' to clear it.",
                    DefaultValue = dto.ApprovalStatusId
                }
            ],
            allowClear: true,
            cancellationToken);

        foreach (var item in approvalStatusOverrides)
            overrides[item.Key] = item.Value;

        return overrides;
    }

    private static SimplicateHourApprovalWriteModel MapHourApprovalToWriteModel(SimplicateHourApproval approval)
        => new()
        {
            Date = approval.Date,
            EmployeeId = approval.Employee?.Id ?? approval.EmployeeId,
            ApprovalStatusId = approval.ApprovalStatus?.Id ?? approval.ApprovalStatusId ?? "null"
        };

    private static object MapHourApprovalToWriteBody(SimplicateHourApprovalWriteModel dto)
        => new
        {
            date = dto.Date,
            employee_id = dto.EmployeeId,
            approvalstatus_id = dto.ApprovalStatusId
        };
}
