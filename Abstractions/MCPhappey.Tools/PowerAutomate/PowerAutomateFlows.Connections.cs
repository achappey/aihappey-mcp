using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PowerAutomate;

public static partial class PowerAutomateFlows
{
    [Description("List existing Dataverse connection references for use by solution-aware cloud flows.")]
    [McpServerTool(Name = "power_automate_connection_references_list", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> ListConnectionReferences(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, int top = 50, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            if (top is < 1 or > 5000) throw new ValidationException("top must be between 1 and 5000.");
            return (await Read(client, Url(dynamicsHost,
                $"connectionreferences?$select=connectionreferenceid,connectionreferencelogicalname,connectionreferencedisplayname,connectorid,connectionid,statecode&$top={top}"), cancellationToken)).ToCallToolResponse();
        });

    [Description("Get the connection-reference mappings embedded in a cloud flow's clientdata.")]
    [McpServerTool(Name = "power_automate_flow_connections_list", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> ListFlowConnections(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
            (ClientData(await GetFlow(client, dynamicsHost, workflowId, cancellationToken))["properties"]?["connectionReferences"]?.DeepClone()
                ?? new JsonObject()).ToCallToolResponse());

    [Description("Map an existing Dataverse connection reference to a connector key in a draft flow. The key must match the action host connectionName. Does not create or delete the shared reference.")]
    [McpServerTool(Name = "power_automate_flow_connections_attach", OpenWorld = false)]
    public static Task<CallToolResult?> AttachConnection(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string connectionName, string connectionReferenceId,
        CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, async client =>
        {
            var reference = await Read(client, Url(dynamicsHost,
                $"connectionreferences({Guid.Parse(connectionReferenceId):D})?$select=connectionreferencelogicalname,connectorid,connectionid,statecode"), cancellationToken);
            var logicalName = reference["connectionreferencelogicalname"]?.GetValue<string>();
            var connectorId = reference["connectorid"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(logicalName) || string.IsNullOrWhiteSpace(connectorId) || reference["statecode"]?.GetValue<int>() != 0)
                throw new ValidationException("An active connection reference with a logical name and connector ID is required.");
            var apiName = connectorId.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            return await Edit(client, dynamicsHost, workflowId, (data, _) =>
            {
                var mappings = data["properties"]?["connectionReferences"] as JsonObject
                    ?? throw new ValidationException("Flow connectionReferences must be an object.");
                var connection = new JsonObject { ["connectionReferenceLogicalName"] = logicalName };
                if (reference["connectionid"]?.GetValue<string>() is { Length: > 0 } connectionId)
                    connection["name"] = connectionId;
                mappings[NonEmpty(connectionName, nameof(connectionName))] = new JsonObject
                {
                    ["runtimeSource"] = "embedded", ["connection"] = connection,
                    ["api"] = new JsonObject { ["name"] = apiName }
                };
            }, cancellationToken);
        });

    [Description("Remove a connection-reference mapping from a draft flow; update actions using its connectionName first.")]
    [McpServerTool(Name = "power_automate_flow_connections_remove", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> RemoveConnection(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string connectionName, CancellationToken cancellationToken = default)
        => Run(services, context, dynamicsHost, client => Edit(client, dynamicsHost, workflowId, (data, definition) =>
        {
            var mappings = data["properties"]?["connectionReferences"] as JsonObject
                ?? throw new ValidationException("Flow connectionReferences must be an object.");
            if (!mappings.ContainsKey(connectionName)) throw new ValidationException("Connection mapping does not exist.");
            if (Section(definition, "actions").Any(a =>
                (a.Value as JsonObject)?["inputs"]?["host"]?["connectionName"]?.GetValue<string>() == connectionName))
                throw new ValidationException("An action still uses this connection mapping.");
            mappings.Remove(connectionName);
        }, cancellationToken));
}
