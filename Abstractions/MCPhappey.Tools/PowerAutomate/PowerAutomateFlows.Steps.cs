using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.PowerAutomate;

public static partial class PowerAutomateFlows
{
    [Description("List WDL triggers on a cloud flow.")]
    [McpServerTool(Name = "power_automate_triggers_list", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> ListTriggers(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => ReadSection(services, context, dynamicsHost, workflowId, "triggers", null, cancellationToken);

    [Description("Get one WDL trigger on a cloud flow.")]
    [McpServerTool(Name = "power_automate_triggers_get", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> GetTrigger(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string triggerName, CancellationToken cancellationToken = default)
        => ReadSection(services, context, dynamicsHost, workflowId, "triggers", triggerName, cancellationToken);

    [Description("Add or replace a WDL trigger on a draft flow. Supply the trigger as a structured object (type, inputs, kind and other type-specific WDL fields), not a JSON string.")]
    [McpServerTool(Name = "power_automate_triggers_upsert", OpenWorld = false)]
    public static Task<CallToolResult?> UpsertTrigger(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string triggerName, JsonObject trigger,
        CancellationToken cancellationToken = default)
        => UpsertStep(services, context, dynamicsHost, workflowId, "triggers", triggerName, trigger, cancellationToken);

    [Description("Remove a WDL trigger from a draft flow; at least one trigger must remain.")]
    [McpServerTool(Name = "power_automate_triggers_remove", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> RemoveTrigger(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string triggerName, CancellationToken cancellationToken = default)
        => RemoveStep(services, context, dynamicsHost, workflowId, "triggers", triggerName, cancellationToken);

    [Description("List WDL actions on a cloud flow.")]
    [McpServerTool(Name = "power_automate_actions_list", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> ListActions(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, CancellationToken cancellationToken = default)
        => ReadSection(services, context, dynamicsHost, workflowId, "actions", null, cancellationToken);

    [Description("Get one WDL action on a cloud flow.")]
    [McpServerTool(Name = "power_automate_actions_get", ReadOnly = true, OpenWorld = false)]
    public static Task<CallToolResult?> GetAction(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string actionName, CancellationToken cancellationToken = default)
        => ReadSection(services, context, dynamicsHost, workflowId, "actions", actionName, cancellationToken);

    [Description("Add or replace a WDL action on a draft flow. Supply a structured object containing type, inputs, runAfter and type-specific fields. No JSON-encoded strings.")]
    [McpServerTool(Name = "power_automate_actions_upsert", OpenWorld = false)]
    public static Task<CallToolResult?> UpsertAction(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string actionName, JsonObject action,
        CancellationToken cancellationToken = default)
        => UpsertStep(services, context, dynamicsHost, workflowId, "actions", actionName, action, cancellationToken);

    [Description("Remove an action from a draft flow. Remove or update dependent actions first.")]
    [McpServerTool(Name = "power_automate_actions_remove", Destructive = true, OpenWorld = false)]
    public static Task<CallToolResult?> RemoveAction(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string dynamicsHost, string workflowId, string actionName, CancellationToken cancellationToken = default)
        => RemoveStep(services, context, dynamicsHost, workflowId, "actions", actionName, cancellationToken);

    private static Task<CallToolResult?> ReadSection(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string host, string id, string section, string? name, CancellationToken ct)
        => Run(services, context, host, async client =>
        {
            var steps = Section(Definition(ClientData(await GetFlow(client, host, id, ct))), section);
            if (name is null) return steps.ToCallToolResponse();
            return (steps[WdlName(name)]?.DeepClone() ?? throw new ValidationException($"{section} entry '{name}' does not exist."))
                .ToCallToolResponse();
        });

    private static Task<CallToolResult?> UpsertStep(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string host, string id, string section, string name, JsonObject step, CancellationToken ct)
        => Run(services, context, host, client => Edit(client, host, id, (_, definition) =>
        {
            var items = Section(definition, section);
            var valid = ValidateStep(step);
            if (section == "actions")
            {
                if (valid["runAfter"] is null) valid["runAfter"] = new JsonObject();
                foreach (var dependency in (JsonObject)valid["runAfter"]!)
                {
                    if (dependency.Key == name || !items.ContainsKey(dependency.Key))
                        throw new ValidationException($"runAfter dependency '{dependency.Key}' must be an existing, different action.");
                    if (dependency.Value is not JsonArray statuses || statuses.Count == 0 || statuses.Any(s => s is not JsonValue))
                        throw new ValidationException("runAfter values must be nonempty arrays of status strings.");
                }
            }
            items[WdlName(name)] = valid;
        }, ct));

    private static Task<CallToolResult?> RemoveStep(IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string host, string id, string section, string name, CancellationToken ct)
        => Run(services, context, host, client => Edit(client, host, id, (_, definition) =>
        {
            var items = Section(definition, section);
            WdlName(name);
            if (!items.ContainsKey(name)) throw new ValidationException($"{section} entry '{name}' does not exist.");
            if (section == "triggers" && items.Count == 1)
                throw new ValidationException("A flow must have at least one trigger.");
            if (section == "actions" && items.Any(item => item.Key != name &&
                (item.Value as JsonObject)?["runAfter"] is JsonObject after && after.ContainsKey(name)))
                throw new ValidationException("Other actions depend on this action; update them first.");
            items.Remove(name);
        }, ct));
}
