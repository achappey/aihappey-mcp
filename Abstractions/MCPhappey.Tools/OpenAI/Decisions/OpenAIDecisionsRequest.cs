using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.OpenAI.Decisions;

public sealed class OpenAIDecisionsRequest
{
    public string Model { get; init; } = OpenAIDecisionsClient.DefaultModel;
    public required JsonNode Input { get; init; }
    public required JsonArray Questions { get; init; }

    [JsonPropertyName("safety_identifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SafetyIdentifier { get; init; }
}

/// <summary>Builds simple questions and validates downloaded official question definitions without coercing values.</summary>
public static class OpenAIDecisionsQuestions
{
    public static JsonArray Predicate(string instructions, string? name = null)
        => Validate(new JsonArray(NewQuestion("predicate", instructions, name)));

    public static JsonArray Choice(string instructions, string[] values, string[]? descriptions = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateDescriptions(values.Length, descriptions);
        var question = NewQuestion("choice", instructions, name);
        question["choices"] = Options(values, descriptions, "value");
        return Validate(new JsonArray(question));
    }

    public static JsonArray Score(string instructions, string[] labels, string[]? descriptions = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ValidateDescriptions(labels.Length, descriptions);
        var question = NewQuestion("score", instructions, name);
        question["levels"] = Options(labels, descriptions, "label");
        return Validate(new JsonArray(question));
    }

    public static JsonArray FromDocument(string json)
    {
        var document = JsonNode.Parse(json.TrimStart('\uFEFF'));
        if (document is not JsonObject root || root["questions"] is not JsonArray questions)
            throw new ArgumentException("The questions file must be a JSON object with a 'questions' array.");

        // Only questions are imported. Evidence and request settings always come from the tool.
        return Validate((JsonArray)questions.DeepClone());
    }

    public static JsonArray Validate(JsonArray questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("At least one question is required.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < questions.Count; index++)
        {
            var path = $"questions[{index}]";
            if (questions[index] is not JsonObject question)
                throw new ArgumentException($"{path} must be an object.");

            RequiredString(question["instructions"], $"{path}.instructions");
            var type = RequiredString(question["type"], $"{path}.type");
            if (question.ContainsKey("name"))
            {
                var name = RequiredString(question["name"], $"{path}.name");
                if (!names.Add(name))
                    throw new ArgumentException("Question names must be unique when provided.");
            }

            switch (type)
            {
                case "predicate":
                    break;
                case "choice":
                    if (question["choices"] is not JsonArray choices || choices.Count is < 2 or > 255)
                        throw new ArgumentException($"{path}.choices must contain between 2 and 255 options.");

                    var values = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in choices)
                    {
                        if (item is not JsonObject option || option["value"] is not JsonValue value)
                            throw new ArgumentException($"{path}.choices entries must have a string or boolean value.");

                        // Prefix the key with its type: true and "true" are different choices.
                        var key = value.TryGetValue<string>(out var text) ? "string:" + text
                            : value.TryGetValue<bool>(out var boolean) ? "boolean:" + boolean
                            : throw new ArgumentException($"{path}.choices values must be strings or booleans.");
                        if (!values.Add(key))
                            throw new ArgumentException($"{path}.choices values must be unique.");
                        OptionalDescription(option, path);
                    }
                    break;
                case "score":
                    if (question["levels"] is not JsonArray levels || levels.Count == 0)
                        throw new ArgumentException($"{path}.levels must be a nonempty array.");
                    foreach (var item in levels)
                    {
                        if (item is not JsonObject level)
                            throw new ArgumentException($"{path}.levels entries must be objects.");
                        RequiredString(level["label"], $"{path}.levels.label");
                        OptionalDescription(level, path);
                    }
                    break;
                default:
                    throw new ArgumentException($"{path}.type must be predicate, choice, or score.");
            }
        }

        return questions;
    }

    private static JsonObject NewQuestion(string type, string instructions, string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        var question = new JsonObject { ["type"] = type, ["instructions"] = instructions };
        if (name is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            question["name"] = name;
        }
        return question;
    }

    private static JsonArray Options(string[] values, string[]? descriptions, string valueProperty)
    {
        var options = new JsonArray();
        for (var index = 0; index < values.Length; index++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(values[index]);
            var option = new JsonObject { [valueProperty] = values[index] };
            if (descriptions is { Length: > 0 })
            {
                if (descriptions[index] is null)
                    throw new ArgumentException("Descriptions must be strings; use an empty string to omit one.");
                if (descriptions[index].Length > 0)
                    option["description"] = descriptions[index];
            }
            options.Add(option);
        }
        return options;
    }

    private static void ValidateDescriptions(int count, string[]? descriptions)
    {
        if (descriptions is { Length: > 0 } && descriptions.Length != count)
            throw new ArgumentException("Descriptions must be omitted or have one entry per choice/level, in the same order.");
    }

    private static string RequiredString(JsonNode? node, string path)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException($"{path} must be a nonempty string.");

    private static void OptionalDescription(JsonObject option, string path)
    {
        if (option.ContainsKey("description")
            && (option["description"] is not JsonValue value || !value.TryGetValue<string>(out _)))
            throw new ArgumentException($"{path}: option descriptions must be strings when provided.");
    }
}
