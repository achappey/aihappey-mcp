using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Sales;
using ModelContextProtocol.Protocol;
using static MCPhappey.Simplicate.Sales.SimplicateSales;

namespace MCPhappey.Tests;

public sealed class SimplicateCustomFieldTests
{
    [Theory]
    [InlineData("input", "Text")]
    [InlineData("textbox", "Text")]
    [InlineData("textarea", "Text")]
    [InlineData("Input", "text")]
    [InlineData(" text ", " Text ")]
    [InlineData(null, "Text")]
    public void Sales_form_includes_custom_scalar_fields_without_a_render_name_allowlist(string? render, string type)
    {
        var request = ElicitFormExtensions.CreateElicitRequestParamsForType(new SimplicateNewSales { Subject = "Deal" });
        var defaults = new Dictionary<string, object?>();
        SimplicateSales.AddCustomFieldSchemas(request, defaults, [Definition(type, render)], null);

        Assert.True(request.RequestedSchema!.Properties!.ContainsKey("subject"));
        var field = Assert.IsType<ElicitRequestParams.StringSchema>(request.RequestedSchema.Properties[Key]);
        Assert.Equal("Customer reference", field.Title);
        Assert.True(defaults.ContainsKey(Key));
        // Check the actual protocol JSON sent to the client, not just an intermediate DTO.
        var wire = JsonSerializer.SerializeToNode(request, JsonSerializerOptions.Web)!;
        Assert.Equal("Customer reference", wire["requestedSchema"]?["properties"]?[Key]?["title"]?.GetValue<string>());
    }

    [Fact]
    public void Dropdown_shows_labels_submits_ids_and_prefills_the_current_selection()
    {
        var definition = Choice();
        definition["mandatory"] = true;
        var request = Request();
        var defaults = new Dictionary<string, object?>();
        SimplicateCustomFields.AddSchemas(request, defaults, [definition], Existing("customfieldoption:two"));

        var field = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties![Key]);
        Assert.Equal(["First choice", "Second choice"], field.OneOf.Select(option => option.Title));
        Assert.Equal(["customfieldoption:one", "customfieldoption:two"], field.OneOf.Select(option => option.Const));
        Assert.Equal("customfieldoption:two", field.Default);
        Assert.Equal(field.Default, defaults[Key]);
        Assert.Contains(Key, request.RequestedSchema.Required!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sales_dropdown_uses_embedded_options_when_catalogue_options_are_missing_or_empty(bool emptyOptions)
    {
        var catalogue = Definition("Text", "dropdown");
        if (emptyOptions) catalogue["options"] = new JsonArray();
        var embedded = Choice();
        embedded["value"] = "customfieldoption:one";
        var existing = new JsonObject { ["custom_fields"] = new JsonArray(embedded) };
        var before = existing.ToJsonString();
        var request = Request();
        SimplicateSales.AddCustomFieldSchemas(request, [], [catalogue], existing);

        var schema = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties![Key]);
        Assert.Equal(2, schema.OneOf.Count);
        Assert.Equal("customfieldoption:one", schema.Default);
        Assert.DoesNotContain("not editable", request.Message);

        var body = new JsonObject();
        SimplicateSales.ApplyCustomFieldAnswers(body, Answers("\"customfieldoption:two\""), [catalogue], existing);
        Assert.Equal("customfieldoption:two", body["custom_fields"]![0]!["value"]!.GetValue<string>());
        Assert.Equal(before, existing.ToJsonString());
        Assert.False(catalogue["options"] is JsonArray { Count: > 0 });
    }

    [Fact]
    public void Sales_record_definitions_render_Propositie_and_Conclusie_without_catalogue_entries()
    {
        // Representative REST response shape from GET sales/sales/{id}; no tenant-specific logic.
        var propositie = Choice();
        propositie["name"] = "propositie";
        propositie["label"] = "Propositie";
        propositie["value"] = "customfieldoption:one";
        var conclusie = Choice();
        conclusie["name"] = "conclusie";
        conclusie["label"] = "Conclusie";
        conclusie["value"] = "customfieldoption:two";
        var existing = new JsonObject { ["custom_fields"] = new JsonArray(propositie, conclusie) };
        var request = Request();
        SimplicateSales.AddCustomFieldSchemas(request, [], [], existing);

        Assert.Equal(2, request.RequestedSchema!.Properties!.Count);
        Assert.Equal("Propositie", request.RequestedSchema.Properties["custom_field:propositie"].Title);
        Assert.Equal("Conclusie", request.RequestedSchema.Properties["custom_field:conclusie"].Title);
        Assert.All(request.RequestedSchema.Properties.Values,
            schema => Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(schema));
        Assert.DoesNotContain("not editable", request.Message);
    }

    [Fact]
    public void Populated_catalogue_options_remain_authoritative_over_older_embedded_options()
    {
        var embedded = Choice();
        embedded["value"] = "customfieldoption:one";
        var catalogue = Choice();
        catalogue["options"]!.AsArray().RemoveAt(0); // The second choice has since been removed.
        var existing = new JsonObject { ["custom_fields"] = new JsonArray(embedded) };
        Assert.Throws<ValidationException>(() => SimplicateSales.ApplyCustomFieldAnswers(new(),
            Answers("\"customfieldoption:two\""), [catalogue], existing));
    }

    [Theory]
    [InlineData("select")]
    [InlineData("radio")]
    [InlineData("DROPDOWN")]
    [InlineData("choice")]
    public void Choice_metadata_is_not_degraded_to_free_text(string render)
    {
        var definition = Choice();
        definition["render_type"] = render;
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [definition], null);
        Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties![Key]);
    }

    [Theory]
    [InlineData("Integer", "0", 0d)]
    [InlineData("Integer", "42", 42d)]
    [InlineData("Decimal", "1234.56", 1234.56d)]
    public void Numeric_defaults_are_typed_and_culture_independent(string type, string value, double expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var request = Request();
            var defaults = new Dictionary<string, object?>();
            SimplicateCustomFields.AddSchemas(request, defaults, [Definition(type, "input")], Existing(value));
            var schema = Assert.IsType<ElicitRequestParams.NumberSchema>(request.RequestedSchema!.Properties![Key]);
            Assert.Equal(expected, schema.Default);
            Assert.IsNotType<string>(defaults[Key]);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Theory]
    [InlineData("Date", "2026-10-26")]
    [InlineData("Time", "13:45:00")]
    [InlineData("DateTime", "2026-10-26 13:45:00")]
    public void Date_and_time_controls_keep_current_values(string type, string value)
    {
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [Definition(type)], Existing(value));
        var schema = Assert.IsType<ElicitRequestParams.StringSchema>(request.RequestedSchema!.Properties![Key]);
        Assert.Equal(value, schema.Default);
        Assert.Contains(type == "Date" ? "yyyy-MM-dd" : "HH:mm", schema.Description);
        if (type == "Date") Assert.Equal("date", schema.Format);
    }

    [Theory]
    [InlineData("Integer", "0", "0")]
    [InlineData("Integer", "-42", "-42")]
    [InlineData("Decimal", "1234.56", "1234.56")]
    [InlineData("Text", "\"A reference\"", "A reference")]
    [InlineData("Date", "\"2024-02-29\"", "2024-02-29")]
    [InlineData("Time", "\"23:59\"", "23:59")]
    [InlineData("Time", "\"23:59:59\"", "23:59:59")]
    [InlineData("DateTime", "\"2026-10-26 13:45:00\"", "2026-10-26 13:45:00")]
    public void Valid_answers_are_serialized_as_writable_name_value_pairs(string type, string json, string expected)
    {
        var body = new JsonObject();
        SimplicateSales.ApplyCustomFieldAnswers(body, Answers(json), [Definition(type)], null);
        var field = Assert.Single(body["custom_fields"]!.AsArray());
        Assert.Equal("customer_reference", field!["name"]!.GetValue<string>());
        Assert.Equal(expected, field["value"]!.GetValue<string>());
        Assert.Equal(2, field.AsObject().Count);
    }

    [Theory]
    [InlineData("Integer", "1.5")]
    [InlineData("Integer", "\"42\"")]
    [InlineData("Integer", "9223372036854775808")]
    [InlineData("Decimal", "\"1,50\"")]
    [InlineData("Decimal", "true")]
    [InlineData("Text", "123")]
    [InlineData("Text", "{}")]
    [InlineData("Text", "[]")]
    [InlineData("Date", "\"2025-02-29\"")]
    [InlineData("Date", "\"26-10-2026\"")]
    [InlineData("Time", "\"24:00\"")]
    [InlineData("DateTime", "\"2026-10-26\"")]
    public void Invalid_answers_fail_before_mutating_the_payload(string type, string json)
    {
        var body = new JsonObject { ["subject"] = "Keep this" };
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(body, Answers(json), [Definition(type)], null));
        Assert.Equal("{\"subject\":\"Keep this\"}", body.ToJsonString());
    }

    [Theory]
    [InlineData("First choice")]
    [InlineData("customfieldoption:other")]
    public void Choices_reject_labels_and_unknown_ids(string answer)
    {
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(new(),
            Answers(JsonSerializer.Serialize(answer)), [Choice()], null));
    }

    [Fact]
    public void Choice_ids_are_written_without_the_display_label_or_definition_metadata()
    {
        var body = new JsonObject();
        SimplicateCustomFields.ApplyAnswers(body, Answers("\"customfieldoption:two\""), [Choice()], null);
        Assert.Equal("customfieldoption:two", body["custom_fields"]![0]!["value"]!.GetValue<string>());
        Assert.DoesNotContain("Second choice", body.ToJsonString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Mandatory_fields_reject_missing_values_on_create(string? json)
    {
        var definition = Definition();
        definition["mandatory"] = true;
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(new(),
            json is null ? new Dictionary<string, JsonElement>() : Answers(json), [definition], null));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void Empty_answers_preserve_current_required_values(string json)
    {
        var definition = Definition();
        definition["mandatory"] = true;
        var body = new JsonObject();
        SimplicateCustomFields.ApplyAnswers(body, Answers(json), [definition], Existing("Keep me"));
        Assert.False(body.ContainsKey("custom_fields"));
    }

    [Fact]
    public void Updates_preserve_unknown_and_unsupported_values_and_do_not_echo_GET_metadata()
    {
        var existing = Existing("Old value");
        existing["custom_fields"]!.AsArray().Add(new JsonObject
        { ["name"] = "not_on_form", ["value"] = 0, ["label"] = "Read-only metadata", ["id"] = "customfield:old" });
        var snapshot = existing.ToJsonString();
        var body = new JsonObject();
        SimplicateCustomFields.ApplyAnswers(body, Answers("\"New value\""), [Definition()], existing);

        var fields = body["custom_fields"]!.AsArray();
        Assert.Equal(2, fields.Count);
        Assert.Equal("New value", fields[0]!["value"]!.GetValue<string>());
        Assert.Equal(0, fields[1]!["value"]!.GetValue<int>());
        Assert.Equal(2, fields[1]!.AsObject().Count);
        Assert.Equal(snapshot, existing.ToJsonString());
    }

    [Fact]
    public void Updates_reject_missing_existing_collection_instead_of_replacing_unknown_values()
    {
        var body = new JsonObject();
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(body,
            Answers("\"New value\""), [Definition()], new JsonObject()));
        Assert.Empty(body);
    }

    [Fact]
    public void Unknown_custom_field_answers_are_rejected()
    {
        var answers = new Dictionary<string, JsonElement> { ["custom_field:invented"] = JsonSerializer.SerializeToElement("oops") };
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(new(), answers, [Definition()], null));
    }

    [Fact]
    public void Current_retired_choice_can_be_preserved_but_not_assigned_to_another_record()
    {
        var existing = Existing("customfieldoption:retired");
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [Choice()], existing);
        var schema = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties![Key]);
        Assert.Contains(schema.OneOf, option => option.Const == "customfieldoption:retired");
        var body = new JsonObject();
        SimplicateCustomFields.ApplyAnswers(body, Answers("\"customfieldoption:retired\""), [Choice()], existing);
        Assert.Empty(body);
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(new(),
            Answers("\"customfieldoption:retired\""), [Choice()], null));
    }

    [Theory]
    [InlineData("checkbox")]
    [InlineData("multiselect")]
    [InlineData("relation")]
    public void Unsupported_controls_are_explained_instead_of_silently_disappearing(string render)
    {
        var definition = Definition("Text", render);
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [definition], Existing("Existing"));
        Assert.False(request.RequestedSchema!.Properties!.ContainsKey(Key));
        Assert.Contains("Customer reference", request.Message);
        Assert.Contains("existing values are preserved", request.Message);
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.ApplyAnswers(new(),
            Answers("\"Unsafe\""), [definition], Existing("Existing")));
    }

    [Fact]
    public void Empty_dropdown_is_not_rendered_as_an_unvalidated_text_input()
    {
        var definition = Definition("Text", "dropdown");
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [definition], null);
        Assert.Empty(request.RequestedSchema!.Properties!);
        Assert.Contains("Customer reference", request.Message);
    }

    [Fact]
    public void Field_order_and_group_description_follow_the_definitions()
    {
        var first = Definition();
        first["position"] = 1;
        first["custom_field_group"] = new JsonObject { ["name"] = "Sales details", ["position"] = "2" };
        var second = Definition();
        second["name"] = "later";
        second["position"] = 3;
        second["custom_field_group"] = first["custom_field_group"]!.DeepClone();
        var request = Request();
        SimplicateCustomFields.AddSchemas(request, [], [second, first], null);
        Assert.Equal([Key, "custom_field:later"], request.RequestedSchema!.Properties!.Keys);
        Assert.Contains("Sales details", request.RequestedSchema.Properties[Key].Description);
    }

    [Fact]
    public void Duplicate_definitions_are_reported_instead_of_overwriting_a_control()
    {
        Assert.Throws<ValidationException>(() => SimplicateCustomFields.AddSchemas(Request(), [], [Definition(), Definition()], null));
    }

    private const string Key = "custom_field:customer_reference";

    private static ElicitRequestParams Request() => new()
    {
        Message = "Review sales details.",
        RequestedSchema = new ElicitRequestParams.RequestSchema
        { Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(), Required = [] }
    };

    private static JsonObject Definition(string type = "Text", string? render = "text") => new()
    {
        ["name"] = "customer_reference", ["label"] = "Customer reference",
        ["render_type"] = render, ["value_type"] = type, ["mandatory"] = false
    };

    private static JsonObject Choice()
    {
        var field = Definition("Text", "dropdown");
        field["options"] = new JsonArray
        {
            new JsonObject { ["id"] = "customfieldoption:two", ["label"] = "Second choice", ["value"] = "second", ["position"] = 2 },
            new JsonObject { ["id"] = "customfieldoption:one", ["label"] = "First choice", ["value"] = "first", ["position"] = 1 }
        };
        return field;
    }

    private static JsonObject Existing(string value) => new()
    {
        ["custom_fields"] = new JsonArray(new JsonObject
        { ["name"] = "customer_reference", ["value"] = value, ["display_value"] = "For display only" })
    };

    private static Dictionary<string, JsonElement> Answers(string json)
        => new() { [Key] = JsonSerializer.Deserialize<JsonElement>(json) };
}
