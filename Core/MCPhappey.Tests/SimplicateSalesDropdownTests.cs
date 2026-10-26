using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Simplicate.Sales;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tests;

public sealed class SimplicateSalesDropdownTests
{
   

    [Fact]
    public void Actual_embedded_value_only_options_also_work_with_empty_catalogue_options()
    {
        var source = ReadFixture();
        var catalogue = source.DeepClone().AsObject();
        foreach (var field in catalogue["custom_fields"]!.AsArray()) field!["options"] = new JsonArray();
        var request = ElicitFormExtensions.CreateElicitRequestParamsForType(new SimplicateSales.SimplicateNewSales());
        SimplicateSales.AddCustomFieldSchemas(request, [], catalogue["custom_fields"]!.AsArray().OfType<JsonObject>(), source);
        Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties!["custom_field:resultaat"]);
        Assert.DoesNotContain("not editable", request.Message);
    }

    [Fact]
    public void Current_value_only_choice_is_prefilled_as_reference_not_number()
    {
        var source = ReadFixture();
        const string selected = "customfieldoption:a9c5859512ed557150835bc0760eb7a4";
        source["custom_fields"]![1]!["value"] = selected;
        var request = ElicitFormExtensions.CreateElicitRequestParamsForType(new SimplicateSales.SimplicateNewSales());
        var defaults = new Dictionary<string, object?>();
        SimplicateSales.AddCustomFieldSchemas(request, defaults, [], source);
        var schema = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(request.RequestedSchema!.Properties!["custom_field:resultaat"]);
        Assert.Equal(selected, schema.Default);
        Assert.Equal(selected, defaults["custom_field:resultaat"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Value_only_dropdown_selection_is_validated_and_saved_using_the_definition_name(bool update)
    {
        var source = ReadFixture();
        var answers = new Dictionary<string, JsonElement>
        {
            ["custom_field:resultaat"] = JsonSerializer.SerializeToElement("customfieldoption:a9c5859512ed557150835bc0760eb7a4")
        };
        var body = new JsonObject();
        SimplicateSales.ApplyCustomFieldAnswers(body, answers,
            source["custom_fields"]!.AsArray().OfType<JsonObject>(), update ? source : null);
        var written = Assert.Single(body["custom_fields"]!.AsArray())!.AsObject();
        Assert.Equal("resultaat", written["name"]!.GetValue<string>());
        Assert.Equal("customfieldoption:a9c5859512ed557150835bc0760eb7a4", written["value"]!.GetValue<string>());
        Assert.Equal(2, written.Count);
    }

    [Theory]
    [InlineData("\"Geen conflict\"")]
    [InlineData("\"customfieldoption:invented\"")]
    [InlineData("1")]
    public void Value_only_choices_reject_labels_unknown_references_and_numeric_values(string json)
    {
        var source = ReadFixture();
        var answers = new Dictionary<string, JsonElement> { ["custom_field:resultaat"] = JsonSerializer.Deserialize<JsonElement>(json) };
        var body = new JsonObject();
        Assert.Throws<ValidationException>(() => SimplicateSales.ApplyCustomFieldAnswers(body, answers, [], source));
        Assert.Empty(body);
    }

    internal static JsonObject ReadFixture()
    {
        using var stream = typeof(SimplicateSalesDropdownTests).Assembly.GetManifestResourceStream(
            "MCPhappey.Tests.Fixtures.SimplicateSalesCustomFields.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
}
