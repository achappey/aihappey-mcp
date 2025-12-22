using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;

namespace MCPhappey.Tools.Graph.Workbooks;

public static partial class GraphWorkbooks
{
   
    static IDictionary<string, object?> ExtractValues(object? content)
    {
        if (content is IDictionary<string, object?> dict) return dict;

        if (content is IDictionary<string, JsonElement> jdict)
            return jdict.ToDictionary(k => k.Key, v => FromJsonElement(v.Value));

        if (content is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in je.EnumerateObject())
                result[prop.Name] = FromJsonElement(prop.Value);
            return result;
        }

        if (content is string s)
        {
            using var doc = JsonDocument.Parse(s);
            return ExtractValues(doc.RootElement);
        }

        // laatste redmiddel
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    static object? FromJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i :
                                el.TryGetDouble(out var d) ? d : (object?)el.GetRawText(),
        JsonValueKind.String => TryParseDate(el.GetString(), out var dt) ? dt : el.GetString(),
        JsonValueKind.Array => el.EnumerateArray().Select(FromJsonElement).ToList(),
        JsonValueKind.Object => el.EnumerateObject()
                                  .ToDictionary(p => p.Name, p => FromJsonElement(p.Value)),
        _ => el.GetRawText(),
    };

    static bool TryParseDate(string? s, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (DateTimeOffset.TryParse(s, out var dto)) { value = dto; return true; }
        if (DateTime.TryParse(s, out var dt)) { value = dt; return true; }
        return false;
    }

    static UntypedNode ToUntyped(object? v) => v switch
    {
        null => new UntypedNull(),
        bool b => new UntypedBoolean(b),
        sbyte or byte or short or ushort or int or uint or long or ulong => new UntypedInteger((int)Convert.ToInt64(v)),
        float or double or decimal => new UntypedFloat((float)Convert.ToDouble(v)),
        DateTime dt => new UntypedString(dt.ToString("o")),
        DateTimeOffset dto => new UntypedString(dto.ToString("o")),
        string s => new UntypedString(s),
        IEnumerable<object?> list => new UntypedArray([.. list.Select(ToUntyped)]),
        IDictionary<string, object?> dict => new UntypedObject(dict.ToDictionary(k => k.Key, v => ToUntyped(v.Value))),
        _ => new UntypedString(v.ToString() ?? string.Empty),
    };

    static UntypedArray BuildValuesNode(IReadOnlyList<string> columns, IDictionary<string, object?> values)
    {
        var rowNodes = new List<UntypedNode>(columns.Count);
        foreach (var col in columns)
            rowNodes.Add(ToUntyped(values.TryGetValue(col, out var v) ? v : null));

        // [[ row ]]
        return new UntypedArray(new List<UntypedArray> { new(rowNodes) });
    }
}
