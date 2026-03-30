using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Flexprice;

public static class FlexpriceCreditGrants
{
    [Description("Create a Flexprice credit grant for a plan or subscription.")]
    [McpServerTool(
        Title = "Flexprice create credit grant",
        Name = "flexprice_credit_grants_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_CreditGrants_Create(
        [Description("Credit grant name.")] string name,
        [Description("Credit grant scope, either PLAN or SUBSCRIPTION.")] string scope,
        [Description("Credit grant cadence, either ONETIME or RECURRING.")] string cadence,
        [Description("Credit amount.")] string credits,
        [Description("Optional plan id when scope is PLAN.")] string? planId,
        [Description("Optional subscription id when scope is SUBSCRIPTION.")] string? subscriptionId,
        [Description("Optional conversion rate.")] string? conversionRate,
        [Description("Optional end date.")] string? endDate,
        [Description("Optional expiration duration.")] int? expirationDuration,
        [Description("Optional expiration duration unit, such as DAY or MONTH.")] string? expirationDurationUnit,
        [Description("Optional expiration type, such as NEVER, DURATION, or BILLING_CYCLE.")] string? expirationType,
        [Description("Optional metadata JSON object.")] string? metadataJson,
        [Description("Optional recurring period, such as MONTHLY.")] string? period,
        [Description("Optional recurring period count.")] int? periodCount,
        [Description("Optional priority.")] int? priority,
        [Description("Optional start date.")] string? startDate,
        [Description("Optional top-up conversion rate.")] string? topupConversionRate,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceCreditGrantResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceCreateCreditGrantInput
                    {
                        Name = name,
                        Scope = scope,
                        Cadence = cadence,
                        Credits = credits,
                        PlanId = planId,
                        SubscriptionId = subscriptionId,
                        ConversionRate = conversionRate,
                        EndDate = endDate,
                        ExpirationDuration = expirationDuration,
                        ExpirationDurationUnit = expirationDurationUnit,
                        ExpirationType = expirationType,
                        MetadataJson = metadataJson,
                        Period = period,
                        PeriodCount = periodCount,
                        Priority = priority,
                        StartDate = startDate,
                        TopupConversionRate = topupConversionRate
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Name, nameof(typed.Name));
                FlexpriceHelpers.ValidateRequired(typed.Scope, nameof(typed.Scope));
                FlexpriceHelpers.ValidateRequired(typed.Cadence, nameof(typed.Cadence));
                FlexpriceHelpers.ValidateRequired(typed.Credits, nameof(typed.Credits));
                FlexpriceHelpers.ValidateCreditGrantScope(typed.Scope, typed.PlanId, typed.SubscriptionId);

                var payload = new FlexpriceCreateCreditGrantRequest
                {
                    Name = typed.Name.Trim(),
                    Scope = typed.Scope.Trim(),
                    Cadence = typed.Cadence.Trim(),
                    Credits = typed.Credits.Trim(),
                    PlanId = FlexpriceHelpers.NullIfWhiteSpace(typed.PlanId),
                    SubscriptionId = FlexpriceHelpers.NullIfWhiteSpace(typed.SubscriptionId),
                    ConversionRate = FlexpriceHelpers.NullIfWhiteSpace(typed.ConversionRate),
                    EndDate = FlexpriceHelpers.NullIfWhiteSpace(typed.EndDate),
                    ExpirationDuration = typed.ExpirationDuration,
                    ExpirationDurationUnit = FlexpriceHelpers.NullIfWhiteSpace(typed.ExpirationDurationUnit),
                    ExpirationType = FlexpriceHelpers.NullIfWhiteSpace(typed.ExpirationType),
                    Metadata = FlexpriceHelpers.ParseJsonObject(typed.MetadataJson, nameof(typed.MetadataJson)),
                    Period = FlexpriceHelpers.NullIfWhiteSpace(typed.Period),
                    PeriodCount = typed.PeriodCount,
                    Priority = typed.Priority,
                    StartDate = FlexpriceHelpers.NullIfWhiteSpace(typed.StartDate),
                    TopupConversionRate = FlexpriceHelpers.NullIfWhiteSpace(typed.TopupConversionRate)
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PostAsync<FlexpriceCreditGrantResponse>("creditgrants", payload, cancellationToken)
                    ?? new FlexpriceCreditGrantResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "POST",
                    endpoint: "/creditgrants",
                    request: payload,
                    response: response,
                    resourceType: "credit-grant",
                    resourceId: response.Id);
            }));

    [Description("Update a Flexprice credit grant by credit grant id.")]
    [McpServerTool(
        Title = "Flexprice update credit grant",
        Name = "flexprice_credit_grants_update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_CreditGrants_Update(
        [Description("Credit grant id.")] string id,
        [Description("Updated credit grant name.")] string? name,
        [Description("Updated metadata JSON object.")] string? metadataJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceCreditGrantResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceUpdateCreditGrantInput
                    {
                        Id = id,
                        Name = name,
                        MetadataJson = metadataJson
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Id, nameof(typed.Id));
                FlexpriceHelpers.ValidateAtLeastOne(typed.Name, typed.MetadataJson);

                var payload = new FlexpriceUpdateCreditGrantRequest
                {
                    Name = FlexpriceHelpers.NullIfWhiteSpace(typed.Name),
                    Metadata = FlexpriceHelpers.ParseJsonObject(typed.MetadataJson, nameof(typed.MetadataJson))
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PutAsync<FlexpriceCreditGrantResponse>($"creditgrants/{FlexpriceHelpers.EscapePath(typed.Id)}", payload, cancellationToken)
                    ?? new FlexpriceCreditGrantResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "PUT",
                    endpoint: "/creditgrants/{id}",
                    request: new
                    {
                        id = typed.Id,
                        body = payload
                    },
                    response: response,
                    resourceType: "credit-grant",
                    resourceId: response.Id ?? typed.Id);
            }));

    [Description("Delete a Flexprice credit grant by id. For subscription-scoped grants you can optionally provide an effective date.")]
    [McpServerTool(
        Title = "Flexprice delete credit grant",
        Name = "flexprice_credit_grants_delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> Flexprice_CreditGrants_Delete(
        [Description("Credit grant id.")] string id,
        [Description("Optional effective date used for subscription scoped credit grants.")] string? effectiveDate,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceSuccessResponse>>(async () =>
            {
                FlexpriceHelpers.ValidateRequired(id, nameof(id));
                await FlexpriceHelpers.ConfirmExactNameAsync<FlexpriceDeleteCreditGrantConfirmation>(requestContext, id, cancellationToken);

                var payload = string.IsNullOrWhiteSpace(effectiveDate)
                    ? null
                    : new FlexpriceDeleteCreditGrantRequest
                    {
                        EffectiveDate = effectiveDate.Trim()
                    };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.DeleteAsync<FlexpriceSuccessResponse>($"creditgrants/{FlexpriceHelpers.EscapePath(id)}", payload, cancellationToken)
                    ?? new FlexpriceSuccessResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "DELETE",
                    endpoint: "/creditgrants/{id}",
                    request: new
                    {
                        id,
                        body = payload
                    },
                    response: response,
                    resourceType: "credit-grant",
                    resourceId: id);
            }));
}

public sealed class FlexpriceCreateCreditGrantInput
{
    [Required]
    [Description("Credit grant name.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Description("Credit grant scope, either PLAN or SUBSCRIPTION.")]
    public string Scope { get; set; } = string.Empty;

    [Required]
    [Description("Credit grant cadence, either ONETIME or RECURRING.")]
    public string Cadence { get; set; } = string.Empty;

    [Required]
    [Description("Credit amount.")]
    public string Credits { get; set; } = string.Empty;

    [Description("Optional plan id when scope is PLAN.")]
    public string? PlanId { get; set; }

    [Description("Optional subscription id when scope is SUBSCRIPTION.")]
    public string? SubscriptionId { get; set; }

    [Description("Optional conversion rate.")]
    public string? ConversionRate { get; set; }

    [Description("Optional end date.")]
    public string? EndDate { get; set; }

    [Description("Optional expiration duration.")]
    public int? ExpirationDuration { get; set; }

    [Description("Optional expiration duration unit, such as DAY or MONTH.")]
    public string? ExpirationDurationUnit { get; set; }

    [Description("Optional expiration type, such as NEVER, DURATION, or BILLING_CYCLE.")]
    public string? ExpirationType { get; set; }

    [Description("Optional metadata JSON object.")]
    public string? MetadataJson { get; set; }

    [Description("Optional recurring period, such as MONTHLY.")]
    public string? Period { get; set; }

    [Description("Optional recurring period count.")]
    public int? PeriodCount { get; set; }

    [Description("Optional priority.")]
    public int? Priority { get; set; }

    [Description("Optional start date.")]
    public string? StartDate { get; set; }

    [Description("Optional top-up conversion rate.")]
    public string? TopupConversionRate { get; set; }
}

public sealed class FlexpriceUpdateCreditGrantInput
{
    [Required]
    [Description("Credit grant id.")]
    public string Id { get; set; } = string.Empty;

    [Description("Updated credit grant name.")]
    public string? Name { get; set; }

    [Description("Updated metadata JSON object.")]
    public string? MetadataJson { get; set; }
}

[Description("Please confirm the credit grant id to delete.")]
public sealed class FlexpriceDeleteCreditGrantConfirmation : IHasName
{
    [Description("Confirm deletion by entering the credit grant id exactly.")]
    public string Name { get; set; } = string.Empty;
}
