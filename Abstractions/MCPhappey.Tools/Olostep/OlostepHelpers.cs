using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MCPhappey.Tools.Olostep;

internal static class OlostepHelpers
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static string[]? ParseDelimitedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0 ? null : parts;
    }

    internal static JsonNode? ParseJsonObjectOrString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            return JsonValue.Create(value.Trim());
        }
    }

    internal static JsonNode? SerializeNode(object? value)
        => value is null ? null : value as JsonNode ?? JsonSerializer.SerializeToNode(value, JsonOptions);

    internal static JsonElement CreateStructuredResponse(string endpoint, object? request, JsonNode? response, params (string Key, object? Value)[] extras)
    {
        var structured = new JsonObject
        {
            ["provider"] = "olostep",
            ["endpoint"] = endpoint,
            ["request"] = SerializeNode(request),
            ["response"] = response?.DeepClone()
        };

        foreach (var (key, value) in extras)
        {
            var node = SerializeNode(value);
            if (node is not null)
                structured[key] = node;
        }

        using var doc = JsonDocument.Parse(structured.ToJsonString());
        return doc.RootElement.Clone();
    }

    internal static void AddIfNotNull(JsonObject json, string key, object? value)
    {
        if (value is null)
            return;

        if (value is string stringValue && string.IsNullOrWhiteSpace(stringValue))
            return;

        json[key] = SerializeNode(value);
    }

    internal static int CountArray(JsonNode? node)
        => node is JsonArray array ? array.Count : 0;

    internal static string? GetString(JsonNode? node, string propertyName)
        => node?[propertyName]?.GetValue<string>();
}
