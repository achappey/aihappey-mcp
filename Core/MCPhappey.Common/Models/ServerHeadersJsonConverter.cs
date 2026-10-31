using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MCPhappey.Common.Models;

/// <summary>Reads and validates registry-style header input definitions. Legacy dictionaries are not supported.</summary>
public sealed partial class ServerHeadersJsonConverter : JsonConverter<List<ServerHeader>>
{
    public override List<ServerHeader>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null)
            return null;

        List<ServerHeader> headers = root.ValueKind switch
        {
            JsonValueKind.Array => root.Deserialize<List<ServerHeader>>(options) ?? [],
            _ => throw new JsonException("Server headers must be an array of registry input definitions. Legacy dictionaries are not supported.")
        };
        Validate(headers);
        return headers;
    }

    public override void Write(Utf8JsonWriter writer, List<ServerHeader> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);

    public static void Validate(IEnumerable<ServerHeader> headers)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Name) || !HeaderName().IsMatch(header.Name) || !names.Add(header.Name))
                throw new JsonException("Header names must be valid, unique HTTP header names (case-insensitive).");

            ValidateInput(header);
            foreach (var variable in header.Variables ?? [])
            {
                if (!VariableName().IsMatch(variable.Key) || variable.Value is null)
                    throw new JsonException($"Invalid variable definition for header '{header.Name}'.");
                ValidateInput(variable.Value);
            }

            foreach (Match match in TemplateVariable().Matches(header.Value ?? ""))
                if (header.Variables?.ContainsKey(match.Groups[1].Value) != true)
                    throw new JsonException($"Header '{header.Name}' references an undefined variable '{match.Groups[1].Value}'.");
        }
    }

    private static void ValidateInput(ServerInput input)
    {
        if (input.Format is not null and not ("string" or "number" or "boolean" or "filepath"))
            throw new JsonException("Unsupported registry input format.");
        if (input.Default is not null && input.Choices?.Contains(input.Default) == false)
            throw new JsonException("An input default must be one of its choices.");
        if (input.Value?.IndexOfAny(['\r', '\n']) >= 0)
            throw new JsonException("Header input values cannot contain line breaks.");
    }

    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderName();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]*$")]
    private static partial Regex VariableName();

    [GeneratedRegex("\\{([^{}]+)\\}")]
    private static partial Regex TemplateVariable();
}
