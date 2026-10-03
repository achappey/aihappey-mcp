using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
            if (page?.Data is null)
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
            ("reason_id", "Sales reason", "/sales/salesreason", "name", dto.ReasonId),
            ("divergent_payment_term_id", "Divergent payment term", "/invoices/paymentterm", "name", dto.DivergentPaymentTermId)
        })
        {
            var items = await ReadLookupAsync(services, context, endpoint, "sort=" + label, ct);
            overrides[key] = LookupSchema(title, value, items, item => Text(item, label), key == "divergent_payment_term_id");
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
        IEnumerable<JsonObject> items, Func<JsonObject, string?> label, bool allowClear = false)
    {
        var options = LookupOptions(items, label).ToList();
        if (allowClear) options.Insert(0, new() { Const = "null", Title = "No divergent payment term (clear override)" });
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

    internal static void AddCustomFieldSchemas(ElicitRequestParams request, Dictionary<string, object?> defaults,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
    {
        foreach (var definition in definitions)
        {
            var name = Text(definition, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var key = CustomFieldPrefix + name;
            var old = (existing?["custom_fields"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(field => Text(field, "name") == name);
            var value = Text(old, "value");
            var schema = CustomFieldSchema(definition, value);
            if (schema is null) continue;
            request.RequestedSchema.Properties[key] = schema;
            if (definition["mandatory"]?.GetValue<bool>() == true)
                request.RequestedSchema.Required.Add(key);
            defaults[key] = CustomDefault(definition, value);
        }
    }

    private static object? CustomDefault(JsonObject definition, string? value)
    {
        if (value is null) return null;
        if (Text(definition, "render_type") == "dropdown") return value;
        return Text(definition, "value_type") switch
        {
            "Integer" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => integer,
            "Decimal" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            _ => value
        };
    }

    private static ElicitRequestParams.PrimitiveSchemaDefinition? CustomFieldSchema(JsonObject definition, string? value)
    {
        var title = Text(definition, "label") ?? Text(definition, "name")!;
        var render = Text(definition, "render_type");
        if (render == "dropdown")
        {
            var options = (definition["options"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            // An empty option list cannot be safely represented as a free-text reference.
            if (options.Count == 0 || options.Any(option => Text(option, "id") is null)) return null;
            return LookupSchema(title, value, options, option => Text(option, "label"));
        }
        // Do not guess encodings for checkbox, multi-select, relation, or other undocumented controls.
        if (render is not (null or "text" or "textarea" or "number" or "date" or "time" or "datetime")) return null;
        var defaultValue = CustomDefault(definition, value);
        return Text(definition, "value_type") switch
        {
            "Integer" or "Decimal" => new ElicitRequestParams.NumberSchema
            { Title = title, Description = Text(definition, "value_type"), Default = defaultValue is string ? null : (dynamic?)defaultValue },
            "Text" => new ElicitRequestParams.StringSchema { Title = title, Default = value },
            "Date" => new ElicitRequestParams.StringSchema { Title = title, Description = "yyyy-MM-dd", Default = value },
            "Time" => new ElicitRequestParams.StringSchema { Title = title, Description = "HH:mm or HH:mm:ss", Default = value },
            "DateTime" => new ElicitRequestParams.StringSchema { Title = title, Description = "yyyy-MM-dd HH:mm:ss", Default = value },
            _ => null
        };
    }

    internal static void ApplyCustomFieldAnswers(JsonObject body, Dictionary<string, JsonElement> answers,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
    {
        var values = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var old in (existing?["custom_fields"] as JsonArray)?.OfType<JsonObject>() ?? [])
            if (Text(old, "name") is string name)
                values[name] = new JsonObject { ["name"] = name, ["value"] = old["value"]?.DeepClone() };
        var changed = false;
        foreach (var definition in definitions)
        {
            var name = Text(definition, "name");
            if (name is null || CustomFieldSchema(definition, Text(values.GetValueOrDefault(name), "value")) is null) continue;
            var key = CustomFieldPrefix + name;
            if (!answers.TryGetValue(key, out var answer) || answer.ValueKind == JsonValueKind.Null)
            {
                if (definition["mandatory"]?.GetValue<bool>() == true
                    && string.IsNullOrWhiteSpace(Text(values.GetValueOrDefault(name), "value")))
                    throw new ValidationException($"Custom field '{Text(definition, "label") ?? name}' is required.");
                continue;
            }
            var value = ValidateCustomFieldAnswer(definition, answer);
            if (definition["mandatory"]?.GetValue<bool>() == true && string.IsNullOrWhiteSpace(value))
                throw new ValidationException($"Custom field '{name}' is required.");
            // Clearing semantics are not documented. Empty answers preserve, rather than delete, existing values.
            if (value.Length == 0) continue;
            values[name] = new JsonObject { ["name"] = name, ["value"] = value };
            changed = true;
        }
        if (changed) body["custom_fields"] = new JsonArray([.. values.Values.Select(field => (JsonNode)field)]);
    }

    private static string ValidateCustomFieldAnswer(JsonObject definition, JsonElement answer)
    {
        var name = Text(definition, "name");
        if (Text(definition, "render_type") == "dropdown")
        {
            var value = answer.ValueKind == JsonValueKind.String ? answer.GetString()! : "";
            var options = (definition["options"] as JsonArray)?.OfType<JsonObject>() ?? [];
            if (!options.Any(option => Text(option, "id") == value))
                throw new ValidationException($"Custom field '{name}' requires a documented option ID.");
            return value;
        }
        if (Text(definition, "value_type") == "Integer")
        {
            if (answer.ValueKind != JsonValueKind.Number || !answer.TryGetInt64(out var integer))
                throw new ValidationException($"Custom field '{name}' must be an integer.");
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (Text(definition, "value_type") == "Decimal")
        {
            if (answer.ValueKind != JsonValueKind.Number || !answer.TryGetDecimal(out var number))
                throw new ValidationException($"Custom field '{name}' must be a decimal.");
            return number.ToString(CultureInfo.InvariantCulture);
        }
        if (answer.ValueKind != JsonValueKind.String) throw new ValidationException($"Custom field '{name}' must be text.");
        var text = answer.GetString()!;
        if (text.Length == 0) return text;
        var formats = Text(definition, "value_type") switch
        {
            "Date" => new[] { "yyyy-MM-dd" },
            "Time" => ["HH:mm", "HH:mm:ss"],
            "DateTime" => ["yyyy-MM-dd HH:mm:ss"],
            _ => null
        };
        if (formats is not null && !DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ValidationException($"Invalid date/time format for custom field '{name}'.");
        return text;
    }
}
