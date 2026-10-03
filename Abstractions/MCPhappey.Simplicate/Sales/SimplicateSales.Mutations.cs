using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Simplicate.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{
    [Description("Create sales. Supply Simplicate IDs, never display names. When supported, a form enriches and confirms the supplied values; otherwise they are submitted directly.")]
    [McpServerTool(Title = "Create new sales in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateSales_CreateSales(
        [Description("Sales subject.")] string subject,
        [Description("Customer organization ID.")] string organizationId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Sales note.")] string? note = null,
        [Description("Own organization profile ID.")] string? myOrganizationProfileId = null,
        [Description("Customer person ID.")] string? personId = null,
        [Description("Contact-person link ID.")] string? contactId = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("Expected closing date, yyyy-MM-dd.")] string? expectedClosingDate = null,
        [Description("Expected revenue.")] decimal? expectedRevenue = null,
        [Description("Integer scoring percentage, 0 to 100.")] int? chanceToScore = null,
        [Description("Responsible employee ID.")] string? responsibleEmployeeId = null,
        [Description("Sales progress ID.")] string? progressId = null,
        [Description("Sales source ID.")] string? sourceId = null,
        [Description("Sales status ID.")] string? statusId = null,
        [Description("Sales reason ID.")] string? reasonId = null,
        [Description("Competitor organization ID.")] string? lostToCompetitorId = null,
        [Description("Payment-term ID, or literal 'null' to clear.")] string? divergentPaymentTermId = null,
        [Description("Comma-separated team IDs; not JSON.")] string? teamIds = null,
        [Description("Whether the sales contact is active.")] bool? contactIsActive = null,
        [Description("Sales contact work function.")] string? contactWorkFunction = null,
        [Description("Sales contact work email.")] string? contactWorkEmail = null,
        [Description("Sales contact work phone.")] string? contactWorkPhone = null,
        [Description("Sales contact work mobile.")] string? contactWorkMobile = null,
        [Description("Person ID inside the contact object.")] string? contactPersonId = null,
        [Description("Use a separate invoice recipient.")] bool? invoiceRecipientIsSeparate = null,
        [Description("Separate invoice-recipient organization ID.")] string? invoiceRecipientOrganizationId = null,
        [Description("Separate invoice-recipient person ID.")] string? invoiceRecipientPersonId = null,
        [Description("Separate invoice-recipient contact-person link ID.")] string? invoiceRecipientContactId = null,
        CancellationToken cancellationToken = default)
        => WriteSalesAsync(serviceProvider, requestContext, null, new SimplicateNewSales
        {
            Subject = subject, OrganizationId = organizationId, Note = note,
            MyOrganizationProfileId = myOrganizationProfileId, PersonId = personId, ContactId = contactId,
            StartDate = startDate, ExpectedClosingDate = expectedClosingDate, ExpectedRevenue = expectedRevenue,
            ChanceToScore = chanceToScore, ResponsibleEmployeeId = responsibleEmployeeId,
            ProgressId = progressId, SourceId = sourceId, StatusId = statusId, ReasonId = reasonId,
            LostToCompetitorId = lostToCompetitorId, DivergentPaymentTermId = divergentPaymentTermId,
            TeamIds = teamIds, ContactIsActive = contactIsActive, ContactWorkFunction = contactWorkFunction,
            ContactWorkEmail = contactWorkEmail, ContactWorkPhone = contactWorkPhone,
            ContactWorkMobile = contactWorkMobile, ContactPersonId = contactPersonId,
            InvoiceRecipientIsSeparate = invoiceRecipientIsSeparate,
            InvoiceRecipientOrganizationId = invoiceRecipientOrganizationId,
            InvoiceRecipientPersonId = invoiceRecipientPersonId, InvoiceRecipientContactId = invoiceRecipientContactId
        }, cancellationToken);

    [Description("Update sales. Omitted inputs preserve existing values. Supply IDs, never display names. Forms prefill existing values when elicitation is supported; otherwise only supplied fields are updated.")]
    [McpServerTool(Title = "Update sales in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateSales_UpdateSales(
        [Description("Exact sales ID.")] string salesId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Sales subject.")] string? subject = null,
        [Description("Sales note.")] string? note = null,
        [Description("Customer organization ID.")] string? organizationId = null,
        [Description("Own organization profile ID.")] string? myOrganizationProfileId = null,
        [Description("Customer person ID.")] string? personId = null,
        [Description("Contact-person link ID.")] string? contactId = null,
        [Description("Start date, yyyy-MM-dd.")] string? startDate = null,
        [Description("Expected closing date, yyyy-MM-dd.")] string? expectedClosingDate = null,
        [Description("Expected revenue.")] decimal? expectedRevenue = null,
        [Description("Integer scoring percentage, 0 to 100.")] int? chanceToScore = null,
        [Description("Responsible employee ID.")] string? responsibleEmployeeId = null,
        [Description("Sales progress ID.")] string? progressId = null,
        [Description("Sales source ID.")] string? sourceId = null,
        [Description("Sales status ID.")] string? statusId = null,
        [Description("Sales reason ID.")] string? reasonId = null,
        [Description("Competitor organization ID.")] string? lostToCompetitorId = null,
        [Description("Payment-term ID, or literal 'null' to clear; omission preserves.")] string? divergentPaymentTermId = null,
        [Description("Comma-separated team IDs; empty explicitly removes all teams; omission preserves.")] string? teamIds = null,
        [Description("Whether the sales contact is active.")] bool? contactIsActive = null,
        [Description("Sales contact work function.")] string? contactWorkFunction = null,
        [Description("Sales contact work email.")] string? contactWorkEmail = null,
        [Description("Sales contact work phone.")] string? contactWorkPhone = null,
        [Description("Sales contact work mobile.")] string? contactWorkMobile = null,
        [Description("Person ID inside the contact object.")] string? contactPersonId = null,
        [Description("Use a separate invoice recipient.")] bool? invoiceRecipientIsSeparate = null,
        [Description("Separate invoice-recipient organization ID.")] string? invoiceRecipientOrganizationId = null,
        [Description("Separate invoice-recipient person ID.")] string? invoiceRecipientPersonId = null,
        [Description("Separate invoice-recipient contact-person link ID.")] string? invoiceRecipientContactId = null,
        CancellationToken cancellationToken = default)
        => WriteSalesAsync(serviceProvider, requestContext, salesId, new SimplicateNewSales
        {
            Subject = subject, OrganizationId = organizationId, Note = note,
            MyOrganizationProfileId = myOrganizationProfileId, PersonId = personId, ContactId = contactId,
            StartDate = startDate, ExpectedClosingDate = expectedClosingDate, ExpectedRevenue = expectedRevenue,
            ChanceToScore = chanceToScore, ResponsibleEmployeeId = responsibleEmployeeId,
            ProgressId = progressId, SourceId = sourceId, StatusId = statusId, ReasonId = reasonId,
            LostToCompetitorId = lostToCompetitorId, DivergentPaymentTermId = divergentPaymentTermId,
            TeamIds = teamIds, ContactIsActive = contactIsActive, ContactWorkFunction = contactWorkFunction,
            ContactWorkEmail = contactWorkEmail, ContactWorkPhone = contactWorkPhone,
            ContactWorkMobile = contactWorkMobile, ContactPersonId = contactPersonId,
            InvoiceRecipientIsSeparate = invoiceRecipientIsSeparate,
            InvoiceRecipientOrganizationId = invoiceRecipientOrganizationId,
            InvoiceRecipientPersonId = invoiceRecipientPersonId, InvoiceRecipientContactId = invoiceRecipientContactId
        }, cancellationToken);

    [Description("Create a sales service using primitive fields. When supported, review a form with default-service and revenue-group dropdowns. Hour-type editing is not supported.")]
    [McpServerTool(Title = "Create sales service in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateSales_CreateService(
        [Description("Service name.")] string name,
        [Description("Sales ID containing the service.")] string salesId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Default-service ID.")] string? defaultServiceId = null,
        [Description("Service explanation.")] string? explanation = null,
        [Description("Supported value: FixedFee.")] string? invoiceMethod = null,
        [Description("Service quantity.")] decimal? amount = null,
        [Description("Unit price.")] decimal? price = null,
        [Description("Track hours.")] bool? trackHours = null,
        [Description("Track costs.")] bool? trackCost = null,
        [Description("Integer item-type display setting.")] int? showItemtype = null,
        [Description("Total in the string format accepted by the API.")] string? total = null,
        [Description("Total including VAT.")] decimal? totalInclVat = null,
        [Description("Service position.")] int? position = null,
        [Description("Month, Quarter, Half_a_year, or Year.")] string? subscriptionCycle = null,
        [Description("Revenue-group ID.")] string? revenueGroupId = null,
        CancellationToken cancellationToken = default)
        => WriteServiceAsync(serviceProvider, requestContext, null, new SimplicateSalesServiceWrite
        {
            Name = name, SalesId = salesId, DefaultServiceId = defaultServiceId, Explanation = explanation,
            InvoiceMethod = invoiceMethod, Amount = amount, Price = price, TrackHours = trackHours,
            TrackCost = trackCost, ShowItemtype = showItemtype, Total = total, TotalInclVat = totalInclVat,
            Position = position, SubscriptionCycle = subscriptionCycle, RevenueGroupId = revenueGroupId
        }, cancellationToken);

    [Description("Update a sales service. Omitted fields are preserved. When supported, review a prefilled form. Hour-type and cost-type collections are never submitted.")]
    [McpServerTool(Title = "Update sales service in Simplicate", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> SimplicateSales_UpdateService(
        [Description("Exact service ID.")] string serviceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Service name.")] string? name = null,
        [Description("Sales ID containing the service.")] string? salesId = null,
        [Description("Default-service ID.")] string? defaultServiceId = null,
        [Description("Service explanation.")] string? explanation = null,
        [Description("Supported value: FixedFee.")] string? invoiceMethod = null,
        [Description("Service quantity.")] decimal? amount = null,
        [Description("Unit price.")] decimal? price = null,
        [Description("Track hours.")] bool? trackHours = null,
        [Description("Track costs.")] bool? trackCost = null,
        [Description("Integer item-type display setting.")] int? showItemtype = null,
        [Description("Total in the string format accepted by the API.")] string? total = null,
        [Description("Total including VAT.")] decimal? totalInclVat = null,
        [Description("Service position.")] int? position = null,
        [Description("Month, Quarter, Half_a_year, or Year.")] string? subscriptionCycle = null,
        [Description("Revenue-group ID.")] string? revenueGroupId = null,
        CancellationToken cancellationToken = default)
        => WriteServiceAsync(serviceProvider, requestContext, serviceId, new SimplicateSalesServiceWrite
        {
            Name = name, SalesId = salesId, DefaultServiceId = defaultServiceId, Explanation = explanation,
            InvoiceMethod = invoiceMethod, Amount = amount, Price = price, TrackHours = trackHours,
            TrackCost = trackCost, ShowItemtype = showItemtype, Total = total, TotalInclVat = totalInclVat,
            Position = position, SubscriptionCycle = subscriptionCycle, RevenueGroupId = revenueGroupId
        }, cancellationToken);

    [Description("Delete a sales service. If form elicitation is supported, type the exact service ID to confirm. Otherwise deletes directly using the supplied ID.")]
    [McpServerTool(Title = "Delete sales service in Simplicate", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SimplicateSales_DeleteService(
        [Description("Exact Simplicate service ID.")] string serviceId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        ValidateReference(serviceId, "serviceId");
        var path = "/sales/service/" + Uri.EscapeDataString(serviceId);
        var success = $"Sales service '{serviceId}' deleted.";
        if (!CanElicit(requestContext))
            return await serviceProvider.DeleteSimplicateResourceAsync(path, success, cancellationToken);

        // Elicit also honors capabilities forwarded in request metadata, unlike the legacy helper's guard.
        var confirmation = requestContext.Elicit(new ConfirmDeleteSimplicateSalesService(), message: serviceId);
        if (!string.Equals(confirmation.Name?.Trim(), serviceId, StringComparison.Ordinal))
            return "Confirmation must match the exact service ID.".ToErrorCallToolResponse();
        return await serviceProvider.DeleteSimplicateResourceAsync(path, success, cancellationToken);
    }
}
