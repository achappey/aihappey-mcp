using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Simplicate.Extensions;

/// <summary>Definition-driven form controls and write validation shared by Simplicate modules.</summary>
internal static class SimplicateCustomFields
{
    internal const string Prefix = "custom_field:";

    internal static void AddSchemas(ElicitRequestParams request, Dictionary<string, object?> defaults,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
    {
        request.RequestedSchema ??= new ElicitRequestParams.RequestSchema();
        request.RequestedSchema.Properties ??= new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        request.RequestedSchema.Required ??= [];
        var unavailable = new List<string>();
        foreach (var (name, definition) in ResolveDefinitions(definitions, existing))
        {
            var value = CurrentValue(existing, name) ?? (existing is null ? Text(definition, "value") : null);
            var schema = CreateSchema(definition, value);
            if (schema is null)
            {
                unavailable.Add($"{Label(definition)} ({Text(definition, "render_type") ?? Text(definition, "value_type") ?? "unknown type"})");
                continue;
            }

            var key = Prefix + name;
            request.RequestedSchema.Properties[key] = schema;
            if (Mandatory(definition) && !request.RequestedSchema.Required.Contains(key))
                request.RequestedSchema.Required.Add(key);
            defaults[key] = DefaultValue(definition, value);
        }

        // MCP forms cannot express arbitrary structured controls. Never silently hide that limitation,
        // or turn a relation/multi-value field into a destructive free-text input.
        if (unavailable.Count > 0)
            request.Message += "\nCustom fields not editable in this form (unsupported controls; existing values are preserved): "
                + string.Join(", ", unavailable) + ".";
    }

    internal static void ApplyAnswers(JsonObject body, IReadOnlyDictionary<string, JsonElement> answers,
        IEnumerable<JsonObject> definitions, JsonObject? existing)
    {
        var fields = ResolveDefinitions(definitions, existing);
        foreach (var key in answers.Keys.Where(key => key.StartsWith(Prefix, StringComparison.Ordinal)))
            if (!fields.ContainsKey(key[Prefix.Length..]))
                throw new ValidationException($"Unknown custom field '{key[Prefix.Length..]}'. Reload the Sales form and try again.");

        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, definition) in fields)
        {
            var current = CurrentValue(existing, name);
            var supplied = answers.TryGetValue(Prefix + name, out var answer)
                && answer.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                && !(answer.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(answer.GetString()));

            if (!supplied)
            {
                if (Mandatory(definition) && string.IsNullOrWhiteSpace(current))
                    throw new ValidationException($"Custom field '{Label(definition)}' is required.");
                continue;
            }

            if (CreateSchema(definition, current) is null)
                throw new ValidationException($"Custom field '{Label(definition)}' has an unsupported control or value type.");

            var value = ValidateAnswer(definition, answer, current);
            if (!string.Equals(value, current, StringComparison.Ordinal)) changes[name] = value;
        }

        if (changes.Count == 0) return;
        if (existing is not null && existing["custom_fields"] is not JsonArray)
            throw new ValidationException("Cannot safely edit custom fields: the existing collection is unavailable.");

        // Merge only writable name/value pairs. Preserve unknown/unsupported fields, not their GET metadata.
        var merged = new JsonArray();
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var old in (existing?["custom_fields"] as JsonArray) ?? [])
        {
            if (old is not JsonObject field || string.IsNullOrWhiteSpace(Text(field, "name")))
                throw new ValidationException("Cannot safely edit custom fields: an existing name/value pair is unavailable.");
            var name = Text(field, "name")!;
            if (!written.Add(name))
                throw new ValidationException($"Cannot safely edit duplicate custom field '{name}'.");
            // Simplicate omits value for unset fields while still returning their definition/options.
            // Do not invent a value for untouched, unset fields or prevent filling one for the first time.
            if (!field.ContainsKey("value") && !changes.ContainsKey(name)) continue;
            merged.Add(new JsonObject
            {
                ["name"] = name,
                ["value"] = changes.TryGetValue(name, out var value) ? JsonValue.Create(value) : field["value"]?.DeepClone()
            });
        }
        foreach (var (name, value) in changes)
            if (written.Add(name)) merged.Add(new JsonObject { ["name"] = name, ["value"] = value });
        body["custom_fields"] = merged;
    }

    private static Dictionary<string, JsonObject> IndexDefinitions(IEnumerable<JsonObject> definitions)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var definition in definitions.OrderBy(definition => Position(definition["custom_field_group"]))
            .ThenBy(Position))
        {
            var name = Text(definition, "name");
            if (string.IsNullOrWhiteSpace(name))
                throw new ValidationException("A Simplicate custom-field definition is missing its name.");
            if (!result.TryAdd(name, definition))
                throw new ValidationException($"Duplicate Simplicate custom-field definition '{name}'.");
        }
        return result;
    }

    private static Dictionary<string, JsonObject> ResolveDefinitions(IEnumerable<JsonObject> definitions, JsonObject? existing)
    {
        var resolved = IndexDefinitions(definitions).ToDictionary(entry => entry.Key,
            entry => entry.Value.DeepClone().AsObject(), StringComparer.Ordinal);
        foreach (var embedded in (existing?["custom_fields"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var name = Text(embedded, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!resolved.TryGetValue(name, out var definition))
            {
                // GET sales/project records can carry the complete field definition themselves.
                // A historical name/value-only pair, however, is not an editable definition.
                if (Text(embedded, "value_type") is not null || Text(embedded, "render_type") is not null
                    || embedded["options"] is JsonArray { Count: > 0 })
                    resolved.Add(name, embedded.DeepClone().AsObject());
                continue;
            }

            foreach (var (key, value) in embedded)
            {
                // Prefer current catalogue metadata when populated. List endpoints can omit
                // expanded options, while the entity's custom_fields already contains them.
                if (definition[key] is null
                    || definition[key] is JsonArray { Count: 0 }
                    || definition[key] is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                        && string.IsNullOrWhiteSpace(text))
                    definition[key] = value?.DeepClone();
            }
        }
        return IndexDefinitions(resolved.Values);
    }

    private static ElicitRequestParams.PrimitiveSchemaDefinition? CreateSchema(JsonObject definition, string? value)
    {
        var render = Normalize(Text(definition, "render_type"));
        if (render is "checkbox" or "multiselect" or "multi_select" or "multi-select" or "relation") return null;
        var title = Label(definition);
        var group = Text(definition["custom_field_group"], "name");
        if (IsChoice(definition))
        {
            var options = Options(definition);
            if (options.Length == 0) return null;
            // Keep an obsolete current choice visible, but it may only be preserved, never assigned anew.
            if (!string.IsNullOrEmpty(value) && options.All(option => option.Const != value))
                options = [.. options, new() { Const = value, Title = $"Current value ({value})" }];
            return new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Default = value,
                OneOf = options
            };
        }

        // value_type is the API's scalar contract. Do not restrict valid scalars to a guessed
        // list of render_type names (e.g. input/text/textarea vary independently of value_type).
        var defaultValue = DefaultValue(definition, value);
        return Normalize(Text(definition, "value_type")) switch
        {
            "integer" or "decimal" => new ElicitRequestParams.NumberSchema
            {
                Title = title,
                Default = defaultValue is null ? null : Convert.ToDouble(defaultValue, CultureInfo.InvariantCulture)
            },
            "text" => new ElicitRequestParams.StringSchema
            { Title = title, Default = value, MinLength = Mandatory(definition) ? 1 : null },
            "date" => new ElicitRequestParams.StringSchema
            { Title = title, Format = "date", Default = value },
            "time" => new ElicitRequestParams.StringSchema
            { Title = title, Default = value },
            "datetime" => new ElicitRequestParams.StringSchema
            { Title = title, Default = value },
            _ => null
        };
    }

    private static object? DefaultValue(JsonObject definition, string? value)
    {
        if (value is null || IsChoice(definition)) return value;
        return Normalize(Text(definition, "value_type")) switch
        {
            "integer" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : null,
            "decimal" => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) ? number : null,
            _ => value
        };
    }

    private static string ValidateAnswer(JsonObject definition, JsonElement answer, string? current)
    {
        var label = Label(definition);
        if (IsChoice(definition))
        {
            if (answer.ValueKind != JsonValueKind.String)
                throw new ValidationException($"Custom field '{label}' requires an option ID.");
            var value = answer.GetString()!;
            if (value != current && Options(definition).All(option => option.Const != value))
                throw new ValidationException($"Custom field '{label}' requires one of the available option IDs, not a label.");
            return value;
        }
        switch (Normalize(Text(definition, "value_type")))
        {
            case "integer":
                if (answer.ValueKind != JsonValueKind.Number || !answer.TryGetInt64(out var integer))
                    throw new ValidationException($"Custom field '{label}' must be an integer.");
                return integer.ToString(CultureInfo.InvariantCulture);
            case "decimal":
                if (answer.ValueKind != JsonValueKind.Number || !answer.TryGetDecimal(out var number))
                    throw new ValidationException($"Custom field '{label}' must be a decimal.");
                return number.ToString(CultureInfo.InvariantCulture);
        }

        if (answer.ValueKind != JsonValueKind.String)
            throw new ValidationException($"Custom field '{label}' must be text.");
        var text = answer.GetString()!;
        var formats = Normalize(Text(definition, "value_type")) switch
        {
            "date" => new[] { "yyyy-MM-dd" },
            "time" => ["HH:mm", "HH:mm:ss"],
            "datetime" => ["yyyy-MM-dd HH:mm:ss"],
            _ => null
        };
        if (formats is not null && !DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _))
            throw new ValidationException($"Custom field '{label}' must use {string.Join(" or ", formats)}.");
        return text;
    }

    private static bool IsChoice(JsonObject definition)
        => Normalize(Text(definition, "render_type")) is "dropdown" or "select" or "radio"
            || definition["options"] is JsonArray { Count: > 0 };

    private static ElicitRequestParams.EnumSchemaOption[] Options(JsonObject definition)
        => [.. ((definition["options"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Where(option => OptionValue(option) is not null)
            .OrderBy(Position)
            .GroupBy(OptionValue, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(option => new ElicitRequestParams.EnumSchemaOption
            { Const = OptionValue(option)!, Title = Text(option, "label") ?? OptionValue(option)! })];

    // Standalone definitions can expose id/label/value, whereas Sales custom_fields
    // embeds label/value pairs with the customfieldoption reference in value, without id.
    private static string? OptionValue(JsonObject option)
        => !string.IsNullOrWhiteSpace(Text(option, "id")) ? Text(option, "id")
            : !string.IsNullOrWhiteSpace(Text(option, "value")) ? Text(option, "value") : null;

    private static string? CurrentValue(JsonObject? existing, string name)
        => (existing?["custom_fields"] as JsonArray)?.OfType<JsonObject>()
            .Where(field => Text(field, "name") == name).Select(field => ScalarText(field["value"])).FirstOrDefault();

    private static string? ScalarText(JsonNode? node)
        => node is JsonValue value ? value.TryGetValue<string>(out var text) ? text : value.ToJsonString() : null;

    private static int Position(JsonNode? node)
        => int.TryParse(ScalarText(node?["position"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
            ? position : int.MaxValue;

    private static bool Mandatory(JsonObject definition)
        => bool.TryParse(ScalarText(definition["mandatory"]), out var mandatory) && mandatory;

    private static string Label(JsonObject definition)
        => string.IsNullOrWhiteSpace(Text(definition, "label")) ? Text(definition, "name")! : Text(definition, "label")!;

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? "";

    private static string? Text(JsonNode? node, string key)
        => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
