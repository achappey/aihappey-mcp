using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.Flexprice;

public static class FlexpriceEnvironments
{
    [Description("Create a Flexprice environment for the current tenant, such as production or staging.")]
    [McpServerTool(
        Title = "Flexprice create environment",
        Name = "flexprice_environments_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_Environments_Create(
        [Description("Environment name.")] string name,
        [Description("Environment type, for example production or staging.")] string type,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceEnvironmentResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceCreateEnvironmentInput
                    {
                        Name = name,
                        Type = type
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Name, nameof(typed.Name));
                FlexpriceHelpers.ValidateRequired(typed.Type, nameof(typed.Type));

                var payload = new FlexpriceCreateEnvironmentRequest
                {
                    Name = typed.Name.Trim(),
                    Type = typed.Type.Trim()
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PostAsync<FlexpriceEnvironmentResponse>("environments", payload, cancellationToken) ?? new FlexpriceEnvironmentResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "POST",
                    endpoint: "/environments",
                    request: payload,
                    response: response,
                    resourceType: "environment",
                    resourceId: response.Id);
            }));

    [Description("Update an existing Flexprice environment by id.")]
    [McpServerTool(
        Title = "Flexprice update environment",
        Name = "flexprice_environments_update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Flexprice_Environments_Update(
        [Description("Environment id.")] string id,
        [Description("Updated environment name.")] string? name,
        [Description("Updated environment type.")] string? type,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<FlexpriceToolResult<FlexpriceEnvironmentResponse>>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new FlexpriceUpdateEnvironmentInput
                    {
                        Id = id,
                        Name = name,
                        Type = type
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                FlexpriceHelpers.ValidateRequired(typed.Id, nameof(typed.Id));
                FlexpriceHelpers.ValidateAtLeastOne(typed.Name, typed.Type);

                var payload = new FlexpriceUpdateEnvironmentRequest
                {
                    Name = FlexpriceHelpers.NullIfWhiteSpace(typed.Name),
                    Type = FlexpriceHelpers.NullIfWhiteSpace(typed.Type)
                };

                var client = FlexpriceHelpers.CreateClient(serviceProvider, requestContext);
                var response = await client.PutAsync<FlexpriceEnvironmentResponse>($"environments/{FlexpriceHelpers.EscapePath(typed.Id)}", payload, cancellationToken)
                    ?? new FlexpriceEnvironmentResponse();

                return FlexpriceHelpers.CreateToolResult(
                    method: "PUT",
                    endpoint: "/environments/{id}",
                    request: new
                    {
                        id = typed.Id,
                        body = payload
                    },
                    response: response,
                    resourceType: "environment",
                    resourceId: response.Id ?? typed.Id);
            }));
}

public sealed class FlexpriceCreateEnvironmentInput
{
    [Required]
    [Description("Environment name.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Description("Environment type, for example production or staging.")]
    public string Type { get; set; } = string.Empty;
}

public sealed class FlexpriceUpdateEnvironmentInput
{
    [Required]
    [Description("Environment id.")]
    public string Id { get; set; } = string.Empty;

    [Description("Updated environment name.")]
    public string? Name { get; set; }

    [Description("Updated environment type.")]
    public string? Type { get; set; }
}
