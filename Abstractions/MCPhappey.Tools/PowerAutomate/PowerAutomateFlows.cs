using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PowerAutomate;

public static partial class PowerAutomateFlows
{
    [Description("List solution-aware Power Automate cloud flows in a Dataverse environment. Returns a page and its OData next link.")]
    [McpServerTool(Name = "power_automate_flows_list", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> List(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, int top = 50, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            if (top is < 1 or > 5000) throw new ValidationException("top must be between 1 and 5000.");
            return (await Read(client, Url(dynamicsHost,
                $"workflows?$filter=category%20eq%205%20and%20type%20eq%201%20and%20modernflowtype%20eq%200&$select=workflowid,name,description,statecode,statuscode,modifiedon,ismanaged&$top={top}"), cancellationToken)).ToCallToolResponse();
        });

    [Description("Get a cloud flow, including its WDL definition and connection-reference mappings in clientdata.")]
    [McpServerTool(Name = "power_automate_flows_get", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> Get(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
            (await GetFlow(client, dynamicsHost, workflowId, cancellationToken)).ToCallToolResponse());

    [Description("Create a draft solution-aware cloud flow with a manual button trigger and no actions. Add actions and connections before enabling.")]
    [McpServerTool(Name = "power_automate_flows_create", OpenWorld = false)]
    public static Task<CallToolResult?> Create(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string solutionUniqueName, string name, string? description = null,
        CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            var body = new JsonObject
            {
                ["category"] = 5, ["type"] = 1, ["primaryentity"] = "none",
                ["name"] = NonEmpty(name, nameof(name)), ["clientdata"] = NewClientData().ToJsonString()
            };
            if (description is not null) body["description"] = description;
            using var request = new HttpRequestMessage(HttpMethod.Post, Url(dynamicsHost, "workflows"))
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.TryAddWithoutValidation("MSCRM.SolutionUniqueName", NonEmpty(solutionUniqueName, nameof(solutionUniqueName)));
            using var response = await client.SendAsync(request, cancellationToken);
            await EnsureSuccess(response, cancellationToken);
            var entityId = response.Headers.TryGetValues("OData-EntityId", out var values) ? values.FirstOrDefault() : response.Headers.Location?.ToString();
            return new JsonObject { ["entityId"] = entityId, ["statecode"] = 0 }.ToCallToolResponse();
        });

    [Description("Update the name or description of a draft, unmanaged cloud flow.")]
    [McpServerTool(Name = "power_automate_flows_update", OpenWorld = false)]
    public static Task<CallToolResult?> Update(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string? name = null, string? description = null,
        CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            var flow = await GetFlow(client, dynamicsHost, workflowId, cancellationToken);
            RequireEditable(flow);
            var body = new JsonObject();
            if (name is not null) body["name"] = NonEmpty(name, nameof(name));
            if (description is not null) body["description"] = description;
            if (body.Count == 0) throw new ValidationException("Provide name or description.");
            return await Patch(client, dynamicsHost, workflowId, flow, body, cancellationToken);
        });

    [Description("Enable a draft cloud flow (statecode 1, statuscode 2). Connections must be valid and accessible to the caller.")]
    [McpServerTool(Name = "power_automate_flows_enable", OpenWorld = false)]
    public static Task<CallToolResult?> Enable(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => ChangeState(services, context, dynamicsHost, workflowId, 0, 1, 2, cancellationToken);

    [Description("Disable an active cloud flow (statecode 0, statuscode 1).")]
    [McpServerTool(Name = "power_automate_flows_disable", OpenWorld = false)]
    public static Task<CallToolResult?> Disable(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => ChangeState(services, context, dynamicsHost, workflowId, 1, 0, 1, cancellationToken);

    private static Task<CallToolResult?> ChangeState(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string host, string id, int expected, int state, int status, CancellationToken ct)
        => Run(services, context, host, async client =>
        {
            var flow = await GetFlow(client, host, id, ct);
            if (flow["statecode"]?.GetValue<int>() != expected)
                throw new ValidationException($"The flow must be in state {expected} for this transition.");
            if (state == 1 && Section(Definition(ClientData(flow)), "actions").Count == 0)
                throw new ValidationException("Add at least one action before enabling the flow.");
            return await Patch(client, host, id, flow,
                new JsonObject { ["statecode"] = state, ["statuscode"] = status }, ct);
        });

    [Description("Delete a draft, unmanaged cloud flow after name confirmation.")]
    [McpServerTool(Name = "power_automate_flows_delete", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> Delete(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            var flow = await GetFlow(client, dynamicsHost, workflowId, cancellationToken);
            RequireEditable(flow);
            return await context.ConfirmAndDeleteAsync<ConfirmDeleteFlow>(flow["name"]?.GetValue<string>() ?? workflowId,
                async _ =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Delete, Url(dynamicsHost, FlowPath(workflowId)));
                    request.Headers.TryAddWithoutValidation("If-Match", flow["@odata.etag"]?.GetValue<string>()
                        ?? throw new InvalidOperationException("Missing flow ETag."));
                    using var response = await client.SendAsync(request, cancellationToken);
                    await EnsureSuccess(response, cancellationToken);
                }, "Flow deleted.", cancellationToken);
        });
}
