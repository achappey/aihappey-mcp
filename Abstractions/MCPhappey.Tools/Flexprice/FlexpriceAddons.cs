using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Flexprice;

public static class FlexpriceAddons
{
    [Description("Create a Flexprice addon that customers can purchase or attach to subscriptions.")]
    [McpServerTool(
        Title = "Flexprice create addon",
        Name = "flexprice_addons_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_Addons_Create(
        [Description("Addon name.")] string name,
        [Description("Lookup key used to resolve the addon externally.")] string lookupKey,
        [Description("Addon type, for example onetime or multiple_instance.")] string type,
        [Description("Optional addon description.")] string? description,
        [Description("Optional metadata JSON object.")] string? metadataJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceAddonResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceCreateAddonInput
                    {
                        Name = name,
                        LookupKey = lookupKey,
                        Type = type,
                        Description = description,
                        MetadataJson = metadataJson
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Name, nameof(typed.Name));
                FlexpriceHelpers.ValidateRequired(typed.LookupKey, nameof(typed.LookupKey));
                FlexpriceHelpers.ValidateRequired(typed.Type, nameof(typed.Type));

                var payload = new FlexpriceCreateAddonRequest
                {
                    Name = typed.Name.Trim(),
                    LookupKey = typed.LookupKey.Trim(),
                    Type = typed.Type.Trim(),
                    Description = FlexpriceHelpers.NullIfWhiteSpace(typed.Description),
                    Metadata = FlexpriceHelpers.ParseJsonObject(typed.MetadataJson, nameof(typed.MetadataJson))
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PostAsync<FlexpriceAddonResponse>("addons", payload, cancellationToken) ?? new FlexpriceAddonResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "POST",
                    endpoint: "/addons",
                    request: payload,
                    response: response,
                    resourceType: "addon",
                    resourceId: response.Id);
            }));

    [Description("Query Flexprice addons using filter and sort options. This wraps the addon search endpoint and returns a paged addon list.")]
    [McpServerTool(
        Title = "Flexprice query addons",
        Name = "flexprice_addons_query",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_Addons_Query(
        [Description("Optional comma separated addon ids.")] string? addonIdsCsv,
        [Description("Optional addon type filter, for example onetime or multiple_instance.")] string? addonType,
        [Description("Optional end time filter.")] string? endTime,
        [Description("Optional expand parameter.")] string? expand,
        [Description("Optional JSON array for advanced filter objects.")] string? filtersJson,
        [Description("Optional page size between 1 and 1000.")] int? limit,
        [Description("Optional comma separated lookup keys.")] string? lookupKeysCsv,
        [Description("Optional offset.")] int? offset,
        [Description("Optional overall order, asc or desc.")] string? order,
        [Description("Optional JSON array for sort objects.")] string? sortJson,
        [Description("Optional start time filter.")] string? startTime,
        [Description("Optional status filter.")] string? status,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceListAddonsResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceQueryAddonsInput
                    {
                        AddonIdsCsv = addonIdsCsv,
                        AddonType = addonType,
                        EndTime = endTime,
                        Expand = expand,
                        FiltersJson = filtersJson,
                        Limit = limit,
                        LookupKeysCsv = lookupKeysCsv,
                        Offset = offset,
                        Order = order,
                        SortJson = sortJson,
                        StartTime = startTime,
                        Status = status
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRange(typed.Limit, 1, 1000, nameof(typed.Limit));
                if (typed.Offset is < 0)
                    throw new ValidationException("offset must be zero or greater.");

                var payload = new FlexpriceAddonFilterRequest
                {
                    AddonIds = FlexpriceHelpers.SplitCsv(typed.AddonIdsCsv),
                    AddonType = FlexpriceHelpers.NullIfWhiteSpace(typed.AddonType),
                    EndTime = FlexpriceHelpers.NullIfWhiteSpace(typed.EndTime),
                    Expand = FlexpriceHelpers.NullIfWhiteSpace(typed.Expand),
                    Filters = FlexpriceHelpers.ParseJsonArray(typed.FiltersJson, nameof(typed.FiltersJson)),
                    Limit = typed.Limit,
                    LookupKeys = FlexpriceHelpers.SplitCsv(typed.LookupKeysCsv),
                    Offset = typed.Offset,
                    Order = FlexpriceHelpers.NullIfWhiteSpace(typed.Order),
                    Sort = FlexpriceHelpers.ParseJsonArray(typed.SortJson, nameof(typed.SortJson)),
                    StartTime = FlexpriceHelpers.NullIfWhiteSpace(typed.StartTime),
                    Status = FlexpriceHelpers.NullIfWhiteSpace(typed.Status)
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PostAsync<FlexpriceListAddonsResponse>("addons/search", payload, cancellationToken)
                    ?? new FlexpriceListAddonsResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "POST",
                    endpoint: "/addons/search",
                    request: payload,
                    response: response,
                    resourceType: "addon-list");
            }));

    [Description("Update a Flexprice addon by addon id.")]
    [McpServerTool(
        Title = "Flexprice update addon",
        Name = "flexprice_addons_update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_Addons_Update(
        [Description("Addon id.")] string id,
        [Description("Updated addon name.")] string? name,
        [Description("Updated addon description.")] string? description,
        [Description("Updated addon metadata JSON object.")] string? metadataJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceAddonResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceUpdateAddonInput
                    {
                        Id = id,
                        Name = name,
                        Description = description,
                        MetadataJson = metadataJson
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Id, nameof(typed.Id));
                FlexpriceHelpers.ValidateAtLeastOne(typed.Name, typed.Description, typed.MetadataJson);

                var payload = new FlexpriceUpdateAddonRequest
                {
                    Name = FlexpriceHelpers.NullIfWhiteSpace(typed.Name),
                    Description = FlexpriceHelpers.NullIfWhiteSpace(typed.Description),
                    Metadata = FlexpriceHelpers.ParseJsonObject(typed.MetadataJson, nameof(typed.MetadataJson))
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PutAsync<FlexpriceAddonResponse>($"addons/{FlexpriceHelpers.EscapePath(typed.Id)}", payload, cancellationToken)
                    ?? new FlexpriceAddonResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "PUT",
                    endpoint: "/addons/{id}",
                    request: new
                    {
                        id = typed.Id,
                        body = payload
                    },
                    response: response,
                    resourceType: "addon",
                    resourceId: response.Id ?? typed.Id);
            }));

    [Description("Delete a Flexprice addon by addon id. The user must confirm the id before the addon is removed.")]
    [McpServerTool(
        Title = "Flexprice delete addon",
        Name = "flexprice_addons_delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> Flexprice_Addons_Delete(
        [Description("Addon id.")] string id,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceSuccessResponse>>(async () =>
            {
                FlexpriceHelpers.ValidateRequired(id, nameof(id));
                await FlexpriceHelpers.ConfirmExactNameAsync<FlexpriceDeleteAddonConfirmation>(requestContext, id, cancellationToken);

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.DeleteAsync<FlexpriceSuccessResponse>($"addons/{FlexpriceHelpers.EscapePath(id)}", null, cancellationToken)
                    ?? new FlexpriceSuccessResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "DELETE",
                    endpoint: "/addons/{id}",
                    request: new { id },
                    response: response,
                    resourceType: "addon",
                    resourceId: id);
            }));
}

public sealed class FlexpriceCreateAddonInput
{
    [Required]
    [Description("Addon name.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Description("Lookup key used to resolve the addon externally.")]
    public string LookupKey { get; set; } = string.Empty;

    [Required]
    [Description("Addon type, for example onetime or multiple_instance.")]
    public string Type { get; set; } = string.Empty;

    [Description("Optional addon description.")]
    public string? Description { get; set; }

    [Description("Optional metadata JSON object.")]
    public string? MetadataJson { get; set; }
}

public sealed class FlexpriceQueryAddonsInput
{
    [Description("Optional comma separated addon ids.")]
    public string? AddonIdsCsv { get; set; }

    [Description("Optional addon type filter, for example onetime or multiple_instance.")]
    public string? AddonType { get; set; }

    [Description("Optional end time filter.")]
    public string? EndTime { get; set; }

    [Description("Optional expand parameter.")]
    public string? Expand { get; set; }

    [Description("Optional JSON array for advanced filter objects.")]
    public string? FiltersJson { get; set; }

    [Description("Optional page size between 1 and 1000.")]
    public int? Limit { get; set; }

    [Description("Optional comma separated lookup keys.")]
    public string? LookupKeysCsv { get; set; }

    [Description("Optional offset.")]
    public int? Offset { get; set; }

    [Description("Optional overall order, asc or desc.")]
    public string? Order { get; set; }

    [Description("Optional JSON array for sort objects.")]
    public string? SortJson { get; set; }

    [Description("Optional start time filter.")]
    public string? StartTime { get; set; }

    [Description("Optional status filter.")]
    public string? Status { get; set; }
}

public sealed class FlexpriceUpdateAddonInput
{
    [Required]
    [Description("Addon id.")]
    public string Id { get; set; } = string.Empty;

    [Description("Updated addon name.")]
    public string? Name { get; set; }

    [Description("Updated addon description.")]
    public string? Description { get; set; }

    [Description("Updated addon metadata JSON object.")]
    public string? MetadataJson { get; set; }
}

[Description("Please confirm the addon id to delete.")]
public sealed class FlexpriceDeleteAddonConfirmation : IHasName
{
    [Description("Confirm deletion by entering the addon id exactly.")]
    public string Name { get; set; } = string.Empty;
}
