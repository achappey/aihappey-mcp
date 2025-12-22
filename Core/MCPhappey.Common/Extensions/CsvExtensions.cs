using System.Text;
using System.Text.Json;

namespace MCPhappey.Common.Extensions;

public static class JsonCsvConverter
{
    public sealed class CsvOptions
    {
        /// <summary>Tekst voor null-waarden (leeg laten geeft lege cel).</summary>
        public string? NullString { get; init; } = null;
        /// <summary>Scheidingsteken voor paden naar geneste objecteigenschappen.</summary>
        public string PathSeparator { get; init; } = ".";
        /// <summary>Kolomvolgorde.</summary>
        public ColumnOrder Order { get; init; } = ColumnOrder.Alphabetical;
        /// <summary>Encoding voor uitvoer.</summary>
        public Encoding Encoding { get; init; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        /// <summary>Regeleinde voor CSV-rijen (RFC 4180 \r\n).</summary>
        public string NewLine { get; init; } = "\r\n";

        public char Delimiter { get; init; } = ','; // of ';' als default voor NL
    }

    public enum ColumnOrder { Encountered, Alphabetical }

    /// <summary>Converteer JSON (string) naar CSV (string).</summary>
    public static string ToCsv(string json, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var rows = FlattenTopLevelArray(doc.RootElement, options);
        return WriteCsvToString(rows, options);
    }

    /// <summary>Schrijf CSV naar een stream. Retourneert aantal rijen (excl. header).</summary>
    public static int WriteCsv(Stream output, string json, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var rows = FlattenTopLevelArray(doc.RootElement, options);
        return WriteCsv(output, rows, options);
    }

    /// <summary>Schrijf al geflatteerde rijen naar CSV.</summary>
    public static int WriteCsv(Stream output, List<Dictionary<string, string?>> rows, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        using var writer = new StreamWriter(output, options.Encoding, leaveOpen: true);

        // Verzamel kolommen
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (columnSet.Add(key)) columns.Add(key);
            }
        }
        if (options.Order == ColumnOrder.Alphabetical)
            columns = [.. columns.OrderBy(c => c, StringComparer.Ordinal)];

        // Header
        writer.WriteLine(string.Join(options.Delimiter, columns.Select(a => EscapeCsv(a, options.Delimiter))));

        // Data
        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                var key = columns[i];
                row.TryGetValue(key, out var value);
                if (value is null) value = options.NullString ?? string.Empty;
                writer.Write(EscapeCsv(value, options.Delimiter));
                if (i < columns.Count - 1) writer.Write(options.Delimiter);
            }
            writer.Write(options.NewLine);
        }

        writer.Flush();
        return rows.Count;
    }

    private static string WriteCsvToString(List<Dictionary<string, string?>> rows, CsvOptions options)
    {
        using var ms = new MemoryStream();
        WriteCsv(ms, rows, options);
        ms.Position = 0;
        return options.Encoding.GetString(ms.ToArray());
    }

    private static List<Dictionary<string, string?>> FlattenTopLevelArray(JsonElement root, CsvOptions options)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Root JSON moet een array zijn (bijv. [ { ... }, { ... } ]).");

        var rows = new List<Dictionary<string, string?>>();
        foreach (var item in root.EnumerateArray())
        {
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            FlattenElement(item, prefix: null, row, options);
            rows.Add(row);
        }
        return rows;
    }

    private static void FlattenElement(JsonElement element, string? prefix, Dictionary<string, string?> row, CsvOptions options)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = Combine(prefix, prop.Name, options.PathSeparator);
                    FlattenElement(prop.Value, key, row, options);
                }
                break;

            case JsonValueKind.Array:
                // Geheel negeren om consistente kolommen te waarborgen
                // (geen indexering, geen joinen).
                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            default:
                var str = PrimitiveToString(element);
                if (prefix is null)
                    row["$"] = str; // wanneer root-item zelf primitief is
                else
                    row[prefix] = str;
                break;
        }
    }

    private static string? PrimitiveToString(JsonElement v)
        => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.TryGetInt64(out var l)
                ? l.ToString()
                : v.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => v.ToString(),
        };

    private static string Combine(string? prefix, string name, string sep)
        => string.IsNullOrEmpty(prefix) ? name : prefix + sep + name;

    private static string EscapeCsv(string? value, char delimiter)
    {
        value ??= string.Empty;
        bool mustQuote = value.Contains('"') || value.Contains(delimiter) || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote) return value;
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            if (c == '"') sb.Append('"');
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }
}