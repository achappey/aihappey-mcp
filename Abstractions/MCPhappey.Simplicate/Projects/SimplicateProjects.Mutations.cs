using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Simplicate.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Projects;

public static partial class SimplicateProjects
{
    [Description("Create a project using primitive values and exact Simplicate IDs. Supported clients review a lookup-backed form; other clients submit the supplied values directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Create new project in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_CreateProject(
        [Description("Project name.")] string name,
        [Description("Project manager employee ID.")] string projectManagerId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Project note.")] string? note = null,
        [Description("Invoice reference.")] string? invoiceReference = null,
        [Description("Own organization profile ID.")] string? myOrganizationProfileId = null,
        [Description("Customer organization ID.")] string? organizationId = null,
        [Description("Customer person ID.")] string? personId = null,
        [Description("Contact-person link ID.")] string? contactId = null,
        [Description("Billable project.")] bool? billable = null,
        [Description("Allow mileage registration.")] bool? canRegisterMileage = null,
        [Description("Project number, not its ID.")] string? projectNumber = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("End date, yyyy-MM-dd.")] string? endDate = null,
        [Description("Project status ID.")] string? projectStatusId = null,
        [Description("Payment term ID or literal string 'null' to unset.")] string? divergentPaymentTermId = null,
        [Description("Use a separate invoice recipient.")] bool? invoiceRecipientIsSeparate = null,
        [Description("Separate invoice-recipient organization ID.")] string? invoiceRecipientOrganizationId = null,
        [Description("Separate invoice-recipient person ID.")] string? invoiceRecipientPersonId = null,
        [Description("Separate invoice-recipient contact-person link ID.")] string? invoiceRecipientContactId = null,
        [Description("Comma-separated team IDs, not JSON.")] string? teamIds = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/project", null, new SimplicateNewProject
        {
            Name = name, ProjectManagerId = projectManagerId, Note = note, InvoiceReference = invoiceReference,
            MyOrganizationProfileId = myOrganizationProfileId, OrganizationId = organizationId,
            PersonId = personId, ContactId = contactId, Billable = billable, CanRegisterMileage = canRegisterMileage,
            ProjectNumber = projectNumber, StartDate = startDate, EndDate = endDate, ProjectStatusId = projectStatusId,
            DivergentPaymentTermId = divergentPaymentTermId, InvoiceRecipientIsSeparate = invoiceRecipientIsSeparate,
            InvoiceRecipientOrganizationId = invoiceRecipientOrganizationId, InvoiceRecipientPersonId = invoiceRecipientPersonId,
            InvoiceRecipientContactId = invoiceRecipientContactId, TeamIds = teamIds
        }, cancellationToken);

    [Description("Update a project. Omitted values are preserved; supplied false and zero values are not omitted. Supported clients review a prefilled form; other clients update supplied values directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Update project in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_UpdateProject(
        [Description("Exact project record ID.")] string projectId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Project name.")] string? name = null,
        [Description("Project manager employee ID.")] string? projectManagerId = null,
        [Description("Project note.")] string? note = null,
        [Description("Invoice reference.")] string? invoiceReference = null,
        [Description("Own organization profile ID.")] string? myOrganizationProfileId = null,
        [Description("Customer organization ID.")] string? organizationId = null,
        [Description("Customer person ID.")] string? personId = null,
        [Description("Contact-person link ID.")] string? contactId = null,
        [Description("Billable project.")] bool? billable = null,
        [Description("Allow mileage registration.")] bool? canRegisterMileage = null,
        [Description("Project number, not its ID.")] string? projectNumber = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("End date, yyyy-MM-dd.")] string? endDate = null,
        [Description("Project status ID.")] string? projectStatusId = null,
        [Description("Payment term ID or literal string 'null' to unset.")] string? divergentPaymentTermId = null,
        [Description("Use a separate invoice recipient.")] bool? invoiceRecipientIsSeparate = null,
        [Description("Separate invoice-recipient organization ID.")] string? invoiceRecipientOrganizationId = null,
        [Description("Separate invoice-recipient person ID.")] string? invoiceRecipientPersonId = null,
        [Description("Separate invoice-recipient contact-person link ID.")] string? invoiceRecipientContactId = null,
        [Description("Comma-separated team IDs. Omit to preserve; empty unassigns known current teams. If the API omits current teams, empty preserves them; use the single-team tool for explicit removal.")] string? teamIds = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/project", projectId, new SimplicateNewProject
        {
            Name = name, ProjectManagerId = projectManagerId, Note = note, InvoiceReference = invoiceReference,
            MyOrganizationProfileId = myOrganizationProfileId, OrganizationId = organizationId,
            PersonId = personId, ContactId = contactId, Billable = billable, CanRegisterMileage = canRegisterMileage,
            ProjectNumber = projectNumber, StartDate = startDate, EndDate = endDate, ProjectStatusId = projectStatusId,
            DivergentPaymentTermId = divergentPaymentTermId, InvoiceRecipientIsSeparate = invoiceRecipientIsSeparate,
            InvoiceRecipientOrganizationId = invoiceRecipientOrganizationId, InvoiceRecipientPersonId = invoiceRecipientPersonId,
            InvoiceRecipientContactId = invoiceRecipientContactId, TeamIds = teamIds
        }, cancellationToken);

    [Description("Create a project service at the documented projects/service endpoint. Supply primitive values, not JSON. Hour-type collections are not edited by this tool.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Create new project service in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_CreateProjectService(
        [Description("Service name.")] string name,
        [Description("Project ID.")] string projectId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Default-service ID.")] string? defaultServiceId = null,
        [Description("Service explanation.")] string? explanation = null,
        [Description("Only FixedFee is documented for writes.")] string? invoiceMethod = null,
        [Description("Service quantity.")] decimal? amount = null,
        [Description("Unit price.")] decimal? price = null,
        [Description("Track hours.")] bool? trackHours = null,
        [Description("Track costs.")] bool? trackCost = null,
        [Description("VAT class ID.")] string? vatClassId = null,
        [Description("Revenue group ID.")] string? revenueGroupId = null,
        [Description("External identifier.")] string? externalId = null,
        [Description("Related service ID.")] string? relatedServiceId = null,
        [Description("Use in resource planner.")] bool? useInResourcePlanner = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("End date, yyyy-MM-dd.")] string? endDate = null,
        [Description("First date allowing hours, yyyy-MM-dd.")] string? writeHoursStartDate = null,
        [Description("Last date allowing hours, yyyy-MM-dd.")] string? writeHoursEndDate = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/service", null, new SimplicateNewProjectService
        {
            Name = name, ProjectId = projectId, DefaultServiceId = defaultServiceId, Explanation = explanation,
            InvoiceMethod = invoiceMethod, Amount = amount, Price = price, TrackHours = trackHours, TrackCost = trackCost,
            VatClassId = vatClassId, RevenueGroupId = revenueGroupId, ExternalId = externalId,
            RelatedServiceId = relatedServiceId, UseInResourcePlanner = useInResourcePlanner,
            StartDate = startDate, EndDate = endDate, WriteHoursStartDate = writeHoursStartDate, WriteHoursEndDate = writeHoursEndDate
        }, cancellationToken);

    [Description("Update documented writable service fields only. Omitted fields, hour/cost types, and GET-only values are preserved. Other existing invoice methods may remain unchanged but only FixedFee can be submitted.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Update project service in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_UpdateProjectService(
        [Description("Exact service ID.")] string serviceId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Service name.")] string? name = null,
        [Description("Default-service ID.")] string? defaultServiceId = null,
        [Description("Service explanation.")] string? explanation = null,
        [Description("Only FixedFee is documented for writes.")] string? invoiceMethod = null,
        [Description("Service quantity.")] decimal? amount = null,
        [Description("Unit price.")] decimal? price = null,
        [Description("Track hours.")] bool? trackHours = null,
        [Description("Track costs.")] bool? trackCost = null,
        [Description("VAT class ID.")] string? vatClassId = null,
        [Description("Revenue group ID.")] string? revenueGroupId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/service", serviceId, new SimplicateProjectServiceWrite
        {
            Name = name, DefaultServiceId = defaultServiceId, Explanation = explanation, InvoiceMethod = invoiceMethod,
            Amount = amount, Price = price, TrackHours = trackHours, TrackCost = trackCost,
            VatClassId = vatClassId, RevenueGroupId = revenueGroupId
        }, cancellationToken);

    [Description("Create an assignment. Supply the exact project-specific hour-type ID. Supported clients review a form; unsupported clients submit primitive inputs directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Create project assignment in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_CreateAssignment(
        [Description("Assignment name.")] string name,
        [Description("Exact project-specific hour-type ID.")] string projectHoursTypeId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Use spread.")] bool? useSpread = null,
        [Description("per_week or total.")] string? hoursType = null,
        [Description("Description.")] string? description = null,
        [Description("Creation timestamp, yyyy-MM-dd HH:mm:ss.")] string? createdAt = null,
        [Description("Update timestamp, yyyy-MM-dd HH:mm:ss.")] string? updatedAt = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("End date, yyyy-MM-dd.")] string? endDate = null,
        [Description("Comma-separated employee IDs, not JSON.")] string? employeeIds = null,
        [Description("Assignment status ID.")] string? statusId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/assignment", null, new SimplicateNewAssignment
        {
            Name = name, ProjectHoursTypeId = projectHoursTypeId, UseSpread = useSpread, HoursType = hoursType,
            Description = description, CreatedAt = createdAt, UpdatedAt = updatedAt, StartDate = startDate,
            EndDate = endDate, EmployeeIds = employeeIds, StatusId = statusId
        }, cancellationToken);

    [Description("Update documented assignment fields. Omitted values are preserved. A supplied employee list is a complete desired selection; empty clearing is not supported. Dates and project-hour-type are create-only in the supplied docs.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Update project assignment in Simplicate")]
    public static Task<CallToolResult?> SimplicateProjects_UpdateAssignment(
        [Description("Exact assignment ID.")] string assignmentId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        [Description("Assignment name.")] string? name = null,
        [Description("Use spread.")] bool? useSpread = null,
        [Description("per_week or total.")] string? hoursType = null,
        [Description("Description.")] string? description = null,
        [Description("Creation timestamp, yyyy-MM-dd HH:mm:ss.")] string? createdAt = null,
        [Description("Update timestamp, yyyy-MM-dd HH:mm:ss.")] string? updatedAt = null,
        [Description("Comma-separated employee IDs; omit to preserve. Empty clearing is not supported.")] string? employeeIds = null,
        [Description("Assignment status ID.")] string? statusId = null,
        CancellationToken cancellationToken = default)
        => WriteAsync(serviceProvider, requestContext, "/projects/assignment", assignmentId, new SimplicateAssignmentWrite
        {
            Name = name, UseSpread = useSpread, HoursType = hoursType, Description = description,
            CreatedAt = createdAt, UpdatedAt = updatedAt, EmployeeIds = employeeIds, StatusId = statusId
        }, cancellationToken);

    [Description("Add an employee to a project. Supported clients review an employee selection form; other clients submit the supplied IDs directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Add a project employee in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_AddProjectEmployee(
        [Description("Project ID.")] string projectId, [Description("Employee ID.")] string employeeId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        var seed = new SimplicateAddProjectEmployee { ProjectId = projectId, EmployeeId = employeeId };
        var overrides = requestContext.SupportsFormElicitation()
            && requestContext.Params?.InputResponses?.ContainsKey("simplicateAddProjectEmployee") != true
            ? await BuildOverridesAsync(serviceProvider, requestContext, seed, cancellationToken) : null;
        var dto = requestContext.Elicit(seed, overrides);
        ValidateId(dto.ProjectId, "projectId", true);
        ValidateId(dto.EmployeeId, "employeeId", true);
        return await SendWriteAsync(serviceProvider, requestContext, "/projects/projectemployee", SerializeWrite(dto), false, cancellationToken);
    }

    [Description("Assign or unassign one project team using scalar inputs. Other team assignments are preserved. Supported clients review a form; other clients submit directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Set project team assignment in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_SetProjectTeam(
        [Description("Project ID.")] string projectId, [Description("Team ID.")] string teamId,
        [Description("True to assign, false to unassign.")] bool assigned,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        ValidateId(projectId, "projectId", true);
        var seed = new SimplicateProjectTeamWrite { TeamId = teamId, Assigned = assigned };
        var overrides = requestContext.SupportsFormElicitation()
            && requestContext.Params?.InputResponses?.ContainsKey("simplicateProjectTeamWrite") != true
            ? await BuildOverridesAsync(serviceProvider, requestContext, seed, cancellationToken) : null;
        var dto = requestContext.Elicit(seed, overrides);
        ValidateId(dto.TeamId, "teamId", true);
        if (dto.Assigned is null) throw new ValidationException("Team assignment value is required.");
        var path = "/projects/project/" + Uri.EscapeDataString(projectId);
        // The documented team payload carries a per-ID assignment flag. A single
        // delta does not depend on GET returning the project's current teams.
        var body = new JsonObject
        {
            ["teams"] = new JsonArray(new JsonObject { ["id"] = dto.TeamId, ["value"] = dto.Assigned.Value })
        };
        return await SendWriteAsync(serviceProvider, requestContext, path, body, true, cancellationToken);
    }

    [Description("Delete a project. Form-capable clients must confirm its ID; clients without form support delete directly using the supplied ID.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Delete project in Simplicate")]
    public static Task<CallToolResult> SimplicateProjects_DeleteProject(
        [Description("Exact project ID.")] string projectId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => DeleteAsync(serviceProvider, requestContext, "/projects/project", projectId, "Project deleted.", cancellationToken);

    [Description("Delete a project service. Form-capable clients must confirm its ID; clients without form support delete directly using the supplied ID.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Delete project service in Simplicate")]
    public static Task<CallToolResult> SimplicateProjects_DeleteProjectService(
        [Description("Exact service ID.")] string serviceId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => DeleteAsync(serviceProvider, requestContext, "/projects/service", serviceId, "Project service deleted.", cancellationToken);

    [Description("Remove an employee from a project using the project-employee membership record ID, NOT the employee ID. Form-capable clients confirm; clients without form support remove directly.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Remove project employee in Simplicate")]
    public static Task<CallToolResult> SimplicateProjects_RemoveProjectEmployee(
        [Description("Exact project-employee membership record ID, not the employee ID.")] string projectEmployeeId,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => DeleteAsync(serviceProvider, requestContext, "/projects/projectemployee", projectEmployeeId, "Project employee removed.", cancellationToken);

    private static Task<CallToolResult> DeleteAsync(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string endpoint, string id, string success, CancellationToken ct)
    {
        ValidateId(id, "recordId", true);
        return context.ConfirmAndDeleteAsync<ConfirmDeleteSimplicateProjectRecord>(id,
            async token => { await services.DeleteSimplicateResourceAsync(endpoint + "/" + Uri.EscapeDataString(id), success, token); },
            success, ct);
    }
}
