using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{
    private static async Task<List<JsonObject>> ReadLookupAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, string query, CancellationToken ct)
    {
        var download = services.GetRequiredService<DownloadService>();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(endpoint);
        var items = new List<JsonObject>();
        for (var offset = 0; ; offset += 100)
        {
            ct.ThrowIfCancellationRequested();
            var page = await download.GetSimplicatePageAsync<JsonObject>(services, context.Server,
                $"{url}?{query}&limit=100&offset={offset}&metadata=count", ct);
            if (page?.Data is null || page.Errors?.Any() == true)
                throw new InvalidOperationException($"Cannot read Simplicate lookup {endpoint}.");
            var batch = page.Data.ToList();
            items.AddRange(batch);
            if (batch.Count < 100 || page.Metadata?.Count > 0 && items.Count >= page.Metadata.Count) break;
        }
        return items;
    }

    private static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>> BuildSalesOverridesAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, SimplicateNewSales dto, CancellationToken ct)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        // Each table is downloaded once even when several form fields use it.
        var organizations = await ReadLookupAsync(services, context, "/crm/organization", "sort=name", ct);
        foreach (var (key, title, value) in new[]
        {
            ("organization_id", "Customer organization", dto.OrganizationId),
            ("lost_to_competitor_id", "Lost to competitor", dto.LostToCompetitorId),
            ("invoice_recipient_organization_id", "Invoice-recipient organization", dto.InvoiceRecipientOrganizationId)
        }) overrides[key] = LookupSchema(title, value, organizations, item => Text(item, "name"));

        var persons = await ReadLookupAsync(services, context, "/crm/person", "sort=full_name", ct);
        foreach (var (key, title, value) in new[]
        {
            ("person_id", "Customer person", dto.PersonId),
            ("contact_person_id", "Person in sales contact", dto.ContactPersonId),
            ("invoice_recipient_person_id", "Invoice-recipient person", dto.InvoiceRecipientPersonId)
        }) overrides[key] = LookupSchema(title, value, persons, item => Text(item, "full_name"));

        var contacts = await ReadLookupAsync(services, context, "/crm/contactperson", "", ct);
        foreach (var (key, title, value) in new[]
        {
            ("contact_id", "Customer contact", dto.ContactId),
            ("invoice_recipient_contact_id", "Invoice-recipient contact", dto.InvoiceRecipientContactId)
        }) overrides[key] = LookupSchema(title, value, contacts,
            item => $"{Text(item["person"], "full_name") ?? Text(item, "person_id")} — {Text(item["organization"], "name")}");

        foreach (var (key, title, endpoint, label, value) in new[]
        {
            ("my_organization_profile_id", "Own organization profile", "/crm/myorganizationprofile", "name", dto.MyOrganizationProfileId),
            ("progress_id", "Sales progress", "/sales/salesprogress", "label", dto.ProgressId),
            ("source_id", "Sales source", "/sales/salessource", "name", dto.SourceId),
            ("status_id", "Sales status", "/sales/salesstatus", "label", dto.StatusId),
            ("reason_id", "Sales reason", "/sales/salesreason", "name", dto.ReasonId)
        })
        {
            var items = await ReadLookupAsync(services, context, endpoint, "sort=" + label, ct);
            overrides[key] = LookupSchema(title, value, items, item => Text(item, label));
        }

        var employeeOverrides = await services.BuildSimplicateEmployeeElicitOverridesAsync<SimplicateNewSales>(context,
            [new SimplicateElicitFieldOverride
            {
                PropertyName = nameof(SimplicateNewSales.ResponsibleEmployeeId), Title = "Responsible employee",
                DefaultValue = dto.ResponsibleEmployeeId
            }], ct);
        foreach (var entry in employeeOverrides) overrides[entry.Key] = entry.Value;
        // Keep an existing employee selectable even if they no longer belong to a current team.
        if (overrides["responsible_employee_id"] is ElicitRequestParams.TitledSingleSelectEnumSchema employees
            && dto.ResponsibleEmployeeId is string employeeId && employees.OneOf.All(option => option.Const != employeeId))
            employees.OneOf = [.. employees.OneOf, new() { Const = employeeId, Title = $"Current employee ({employeeId})" }];

        var teams = await ReadLookupAsync(services, context, "/hrm/team", "sort=name", ct);
        var options = LookupOptions(teams, item => Text(item, "name")).ToList();
        foreach (var id in SplitTeamIds(dto.TeamIds ?? ""))
            if (options.All(option => option.Const != id)) options.Add(new() { Const = id, Title = $"Current team ({id})" });
        if (options.Count > 0)
            overrides["team_ids"] = new ElicitRequestParams.TitledMultiSelectEnumSchema
            {
                Title = "Teams", Description = "Select teams; deselect a team to remove its assignment.",
                Default = SplitTeamIds(dto.TeamIds ?? ""),
                Items = new ElicitRequestParams.TitledEnumItemsSchema { AnyOf = [.. options] }
            };
        return overrides;
    }

    private static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>> BuildServiceOverridesAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, SimplicateSalesServiceWrite dto, CancellationToken ct)
    {
        var defaults = await ReadLookupAsync(services, context, "/services/defaultservice", "sort=name", ct);
        var groups = await ReadLookupAsync(services, context, "/sales/revenuegroup", "sort=label", ct);
        return new()
        {
            ["default_service_id"] = LookupSchema("Default service", dto.DefaultServiceId, defaults, item => Text(item, "name")),
            ["revenue_group_id"] = LookupSchema("Revenue group", dto.RevenueGroupId, groups, item => Text(item, "label")),
            ["invoice_method"] = FixedChoiceSchema("Invoice method", dto.InvoiceMethod, ["FixedFee"]),
            ["subscription_cycle"] = FixedChoiceSchema("Subscription cycle", dto.SubscriptionCycle, ["Month", "Quarter", "Half_a_year", "Year"])
        };
    }

    internal static ElicitRequestParams.PrimitiveSchemaDefinition LookupSchema(string title, string? current,
        IEnumerable<JsonObject> items, Func<JsonObject, string?> label)
    {
        var options = LookupOptions(items, label).ToList();
        if (current is not null && options.All(option => option.Const != current))
            options.Add(new() { Const = current, Title = $"Current value ({current})" });
        return options.Count == 0
            ? new ElicitRequestParams.StringSchema { Title = title, Description = "Simplicate ID, not a display name.", Default = current }
            : new ElicitRequestParams.TitledSingleSelectEnumSchema
            { Title = title, Description = "Select a label; the submitted value is its Simplicate ID.", Default = current, OneOf = [.. options] };
    }

    private static IEnumerable<ElicitRequestParams.EnumSchemaOption> LookupOptions(IEnumerable<JsonObject> items,
        Func<JsonObject, string?> label)
        => items.Where(item => Text(item, "id") is not null && item["blocked"]?.GetValue<bool>() != true)
            .GroupBy(item => Text(item, "id"), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => label(item) ?? Text(item, "id"), StringComparer.OrdinalIgnoreCase)
            .Select(item => new ElicitRequestParams.EnumSchemaOption
            { Const = Text(item, "id")!, Title = label(item) ?? Text(item, "id")! });

    private static ElicitRequestParams.TitledSingleSelectEnumSchema FixedChoiceSchema(string title, string? current, string[] values)
        => new() { Title = title, Default = current, OneOf = [.. values.Select(value => new ElicitRequestParams.EnumSchemaOption
        { Const = value, Title = value == "Half_a_year" ? "Half a year" : value })] };

    private const string CustomFieldPrefix = "custom_field:";

    // The initial form has already supplied the type/option schema. On save, map the
    // submitted primitives without re-downloading definitions or validating lookup IDs.
    // Simplicate is authoritative for field names, options and reference validity.
    internal static void ApplySubmittedCustomFields(JsonObject body, Dictionary<string, JsonElement> answers,
        JsonObject? existing)
    {
        var submitted = answers.Where(answer => answer.Key.StartsWith(CustomFieldPrefix, StringComparison.Ordinal)
            && answer.Value.ValueKind != JsonValueKind.Null).ToArray();
        if (submitted.Length == 0) return;
        if (existing is not null && existing["custom_fields"] is not JsonArray)
            throw new ValidationException("Cannot safely edit custom fields: the existing collection is unavailable.");
        var values = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var old in (existing?["custom_fields"] as JsonArray)?.OfType<JsonObject>() ?? [])
            if (Text(old, "name") is string name)
                values[name] = new JsonObject { ["name"] = name, ["value"] = old["value"]?.DeepClone() };
        var changed = false;
        foreach (var (key, answer) in submitted)
        {
            var name = key[CustomFieldPrefix.Length..];
            if (string.IsNullOrWhiteSpace(name)) throw new ValidationException("Custom field name is required.");
            var value = answer.ValueKind switch
            {
                JsonValueKind.String => answer.GetString()!,
                JsonValueKind.Number => answer.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new ValidationException("Custom field values must be primitives, not objects or arrays.")
            };
            // Clearing semantics remain undocumented: empty answers preserve old values.
            if (value.Length == 0) continue;
            values[name] = new JsonObject { ["name"] = name, ["value"] = value };
            changed = true;
        }
        if (changed) body["custom_fields"] = new JsonArray([.. values.Values.Select(field => (JsonNode)field)]);
    }

    internal static void AddCustomFieldSchemas(ElicitRequestParams request, Dictionary<string, object?> defaults,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
        => SimplicateCustomFields.AddSchemas(request, defaults, definitions, existing);

    internal static void ApplyCustomFieldAnswers(JsonObject body, Dictionary<string, JsonElement> answers,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
        => SimplicateCustomFields.ApplyAnswers(body, answers, definitions, existing);
}
