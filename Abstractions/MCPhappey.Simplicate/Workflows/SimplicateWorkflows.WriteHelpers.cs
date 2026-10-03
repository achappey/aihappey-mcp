using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Workflows;

public static partial class SimplicateWorkflows
{
    private const string WorkflowEndpoint = "/workflows/workflow";
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static bool HasResponse<T>(RequestContext<CallToolRequestParams> context)
        => context.Params?.InputResponses?.ContainsKey(char.ToLowerInvariant(typeof(T).Name[0]) + typeof(T).Name[1..]) == true;

    private static string WorkflowPath(string workflowId)
    {
        ValidateId(workflowId, nameof(workflowId), required: true);
        return WorkflowEndpoint + "/" + Uri.EscapeDataString(workflowId);
    }

    private static async Task<CallToolResult?> CreateWorkflowAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, SimplicateWorkflowCreate incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SimplicateWorkflowCreate dto;
        // Consume resumed input before doing any optional lookup. A declined response never writes.
        if (HasResponse<SimplicateWorkflowCreate>(context))
            dto = Merge(incoming, context.Elicit(incoming));
        else if (context.SupportsFormElicitation())
        {
            var overrides = await EmployeeOverridesAsync<SimplicateWorkflowCreate>(services, context,
                nameof(SimplicateWorkflowCreate.DestinationEmployeeId), incoming.DestinationEmployeeId, ct);
            dto = Merge(incoming, context.Elicit(incoming, overrides));
        }
        else dto = incoming;

        RequireText(dto.Title, "title");
        RequireText(dto.Description, "description");
        ValidateId(dto.DefaultWorkflowId, "defaultWorkflowId", required: true);
        ValidateId(dto.DestinationEmployeeId, "destinationEmployeeId", required: true);
        var body = MapCreateBody(dto);
        ValidateDeadlines(body, null, DateOnly.FromDateTime(DateTime.Today));
        return await SendAsync(services, context, WorkflowEndpoint, body, update: false, ct);
    }

    private static async Task<CallToolResult?> UpdateWorkflowAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string workflowId, SimplicateWorkflowEdit incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = WorkflowPath(workflowId);
        var resumed = HasResponse<SimplicateWorkflowEdit>(context);
        var submitted = resumed ? context.Elicit(incoming) : null;
        if (!resumed && !context.SupportsFormElicitation() && Serialize(incoming).Count == 0)
            return "No writable values were supplied; no mutation performed.".ToTextCallToolResponse();

        var existing = await ReadWorkflowAsync(services, context, path, ct);
        SimplicateWorkflowEdit dto;
        if (resumed)
            dto = Merge(incoming, submitted!);
        else if (context.SupportsFormElicitation())
        {
            var seed = Merge(existing.Deserialize<SimplicateWorkflowEdit>(WriteOptions)!, incoming);
            dto = Merge(seed, context.Elicit(seed));
        }
        else dto = incoming;

        // Diff the scalar whitelist: untouched historical deadlines must not be resubmitted or rejected.
        var body = MapEditBody(dto, existing);
        if (body.ContainsKey("title")) RequireText(Text(body, "title"), "title");
        if (body.ContainsKey("description")) RequireText(Text(body, "description"), "description");
        ValidateDeadlines(body, existing, DateOnly.FromDateTime(DateTime.Today));
        return await SendAsync(services, context, path, body, update: true, ct);
    }

    private static async Task<CallToolResult?> ExecuteActionAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string workflowId, SimplicateWorkflowAction incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = WorkflowPath(workflowId);
        var resumed = HasResponse<SimplicateWorkflowAction>(context);
        var dto = resumed ? Merge(incoming, context.Elicit(incoming)) : incoming;
        var workflow = await ReadWorkflowAsync(services, context, path, ct);
        if (!resumed && context.SupportsFormElicitation())
        {
            var overrides = await EmployeeOverridesAsync<SimplicateWorkflowAction>(services, context,
                nameof(SimplicateWorkflowAction.NextEmployeeId), dto.NextEmployeeId, ct);
            var actions = (workflow["actions"] as JsonArray)?.OfType<JsonObject>() ?? [];
            var choices = actions.Where(action => Text(action, "id") is not null)
                .Select(action => new ElicitRequestParams.EnumSchemaOption
                {
                    Const = Text(action, "id")!, Title = Text(action, "name") ?? Text(action, "id")!
                }).ToArray();
            if (choices.Length > 0)
                overrides["id"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
                {
                    Title = "Current task action", Default = dto.ActionId, OneOf = choices
                };
            dto = Merge(dto, context.Elicit(dto, overrides));
        }
        ValidateAction(dto, workflow);
        return await SendAsync(services, context, path, new JsonObject { ["to_task"] = Serialize(dto) }, update: true, ct);
    }

    private static async Task<CallToolResult?> TransferWorkflowAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string workflowId, SimplicateWorkflowTransfer incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = WorkflowPath(workflowId);
        var resumed = HasResponse<SimplicateWorkflowTransfer>(context);
        var dto = resumed ? Merge(incoming, context.Elicit(incoming)) : incoming;
        var workflow = await ReadWorkflowAsync(services, context, path, ct);
        if (!resumed && context.SupportsFormElicitation())
        {
            var overrides = await EmployeeOverridesAsync<SimplicateWorkflowTransfer>(services, context,
                nameof(SimplicateWorkflowTransfer.TransferToEmployeeId), dto.TransferToEmployeeId, ct);
            dto = Merge(dto, context.Elicit(dto, overrides));
        }
        ValidateId(dto.TransferToEmployeeId, "transferToEmployeeId", required: true);
        if (workflow["current_task"] is not JsonObject task || task["can_be_transferred"]?.GetValue<bool>() != true)
            throw new ValidationException("The current workflow task does not allow transfer.");
        return await SendAsync(services, context, path, Serialize(dto), update: true, ct);
    }

    private static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>> EmployeeOverridesAsync<T>(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, string property, string? value, CancellationToken ct)
        where T : class
    {
        try
        {
            var overrides = await services.BuildSimplicateEmployeeElicitOverridesAsync<T>(context,
                [new SimplicateElicitFieldOverride { PropertyName = property, Title = "Employee", DefaultValue = value }], ct);
            return overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // A lookup is form enrichment only: retain the primitive ID field if it is unavailable.
            return [];
        }
    }

    private static async Task<JsonObject> ReadWorkflowAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string path, CancellationToken ct)
    {
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var result = await services.GetRequiredService<DownloadService>()
            .GetSimplicateItemAsync<JsonObject>(services, context.Server, url, ct);
        if (result?.Data is null || result.Errors?.Any() == true)
            throw new InvalidOperationException("The current Simplicate workflow could not be read; no mutation performed.");
        return result.Data;
    }

    private static async Task<CallToolResult?> SendAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string path, JsonObject body, bool update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (body.Count == 0)
            return "No writable values changed; no mutation performed.".ToTextCallToolResponse();
        var scraper = services.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var content = update
            ? await scraper.PutSimplicateItemAsync(services, url, body, ct)
            : await scraper.PostSimplicateItemAsync(services, url, body, context, ct);
        if (content is null)
            return "Simplicate returned no result. Check the configured credentials.".ToErrorCallToolResponse();
        return content.ToCallToolResult();
    }

    private static JsonObject Serialize<T>(T dto)
        => JsonSerializer.SerializeToNode(dto, WriteOptions)!.AsObject();

    private static T Merge<T>(T seed, T incoming) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
            if (property.GetValue(incoming) is { } value) property.SetValue(seed, value);
        return seed;
    }

    internal static JsonObject MapCreateBody(SimplicateWorkflowCreate dto)
    {
        var body = Serialize(dto);
        var links = new JsonObject();
        foreach (var key in new[] { "sales_id", "project_id", "employee_id", "invoice_id", "person_id", "organization_id" })
        {
            if (!body.ContainsKey(key)) continue;
            ValidateId(Text(body, key), key, required: true);
            links[key] = body[key]?.DeepClone();
            body.Remove(key);
        }
        if (links.Count > 0) body["linked_to"] = links;
        return body;
    }

    internal static JsonObject MapEditBody(SimplicateWorkflowEdit dto, JsonObject existing)
    {
        // Explicit whitelist prevents derived form fields, collections and command replay on ordinary updates.
        var body = new JsonObject();
        foreach (var (key, value) in new[]
        {
            ("title", dto.Title), ("description", dto.Description),
            ("deadline_step", dto.DeadlineStep), ("deadline_workflow", dto.DeadlineWorkflow)
        })
            if (value is not null && !string.Equals(value, Text(existing, key), StringComparison.Ordinal))
                body[key] = value;
        return body;
    }

    internal static void ValidateAction(SimplicateWorkflowAction dto, JsonObject workflow)
    {
        ValidateId(dto.ActionId, "actionId", required: true);
        ValidateId(dto.NextEmployeeId, "nextEmployeeId");
        var action = (workflow["actions"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(item => string.Equals(Text(item, "id"), dto.ActionId, StringComparison.Ordinal));
        if (action is null)
            throw new ValidationException("actionId must be one of the current workflow actions[].id values, not a task ID.");
        if (action["is_response_required"]?.GetValue<bool>() == true)
            RequireText(dto.Reaction, "reaction");
        // The API resolves fixed destinations, return-to-sender rules and recipients omitted from the DTO.
        // Only enforce destination information that is actually present; never guess it from a task ID.
        if (action["to_task"] is JsonObject destination && destination["employees"] is JsonArray employees)
        {
            var ids = employees.OfType<JsonObject>().Select(employee => Text(employee, "id"))
                .Where(id => id is not null).ToArray();
            if (destination["is_return_to_sender"]?.GetValue<bool>() == true
                && Text(workflow["created_by"] as JsonObject, "id") is string sender)
                ids = [.. ids, sender];
            if (ids.Length == 0 && dto.NextEmployeeId is null)
                throw new ValidationException("The next task resolves to no employee; nextEmployeeId is required.");
            if (destination["can_change_destination"]?.GetValue<bool>() == false
                && dto.NextEmployeeId is not null && !ids.Contains(dto.NextEmployeeId, StringComparer.Ordinal))
                throw new ValidationException("The next task has a fixed destination; choose one of its resolved employees.");
        }
    }

    internal static void ValidateDeadlines(JsonObject changes, JsonObject? existing, DateOnly today)
    {
        if (!changes.ContainsKey("deadline_step") && !changes.ContainsKey("deadline_workflow")) return;
        DateOnly? Parse(string? value, string field)
        {
            if (value is null) return null;
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ValidationException($"{field} must use yyyy-MM-dd.");
            return date;
        }
        foreach (var key in new[] { "deadline_step", "deadline_workflow" })
            if (changes.ContainsKey(key) && Parse(Text(changes, key), key) is { } date && date < today)
                throw new ValidationException($"{key} must not be before today.");
        var step = Parse(Text(changes, "deadline_step") ?? Text(existing, "deadline_step"), "deadline_step");
        var workflow = Parse(Text(changes, "deadline_workflow") ?? Text(existing, "deadline_workflow"), "deadline_workflow");
        if (step.HasValue && workflow.HasValue && step > workflow)
            throw new ValidationException("deadline_step must not exceed deadline_workflow.");
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException($"{field} must not be empty.");
    }

    private static void ValidateId(string? value, string field, bool required = false)
    {
        if (value is null && !required) return;
        RequireText(value, field);
        var colon = value!.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1 || value.Any(char.IsWhiteSpace)
            || value.Any(character => character is '/' or '\\' or '?' or '#' or ','))
            throw new ValidationException($"{field} must be a complete Simplicate ID (prefix:identifier), not a display name.");
    }

    private static string? Text(JsonObject? node, string key)
        => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
