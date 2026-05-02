using System.Globalization;
using System.Text.Json;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Tools.Extensions;

public static class HttpExtensions
{


    public static async Task<CallToolResult?> ToCallToolResponseOrErrorAsync(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            // Use your extension method to generate the error response
            return errorMessage.ToErrorCallToolResponse();
        }

        // You'd handle the happy path in your main logic, or you could deserialize here as needed
        // This is just the error shortcut method.
        return null;
    }


    public static ElicitRequestParams.PrimitiveSchemaDefinition? ToElicitSchemaDef(this ColumnDefinition col,
        object? defaultValue = null)
    {
        var title = col.DisplayName ?? col.Name;
        var desc = col.Description ?? "";

        if (col.Text != null)
        {
            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = ToDefaultString(defaultValue)
            };
        }

        if (col.Number != null || col.Currency != null)
        {
            return new ElicitRequestParams.NumberSchema
            {
                Title = title,
                Description = desc,
                Minimum = col.Number?.Minimum,
                Maximum = col.Number?.Maximum,
                Default = ToDefaultDouble(defaultValue)

            };
        }

        if (col.Choice != null)
        {
            return new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = title,
                Description = desc,
                Default = ToDefaultString(defaultValue),
                OneOf = col.Choice.Choices?
                    .Select(a => new ElicitRequestParams.EnumSchemaOption
                    {
                        Title = a,
                        Const = a,
                    })
                    .ToList()
                    ?? []
            };
        }

        if (col.DateTime != null)
        {
            var fmt = col.DateTime.Format?.ToString();

            var isDateOnly = string.Equals(
                fmt,
                "dateOnly",
                StringComparison.OrdinalIgnoreCase);

            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = ToDefaultString(defaultValue),
                Format = isDateOnly ? "date" : "date-time"
            };
        }


        if (col.Boolean != null)
        {
            return new ElicitRequestParams.BooleanSchema
            {
                Title = title,
                Description = desc,
                Default = ToDefaultBool(defaultValue)
                    ?? (col.DefaultValue?.Value == null ? null : col.DefaultValue.Value == "1")

            };
        }

        if (col.HyperlinkOrPicture != null)
        {
            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = ToDefaultString(defaultValue),
                Format = "uri"
            };
        }

        return null;
    }



    private static string? ToDefaultString(object? value)
    {
        if (value is null) return null;

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        if (value is DateTimeOffset dto)
            return dto.ToUniversalTime().ToString("o");

        if (value is DateTime dt)
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static double? ToDefaultDouble(object? value)
    {
        if (value is null) return null;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var d))
                return d;

            if (je.ValueKind == JsonValueKind.String &&
                double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        if (value is double d1) return d1;
        if (value is float f) return f;
        if (value is decimal m) return (double)m;
        if (value is int i) return i;
        if (value is long l) return l;

        return double.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : null;
    }

    private static bool? ToDefaultBool(object? value)
    {
        if (value is null) return null;

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when je.TryGetInt32(out var i) => i != 0,
                JsonValueKind.String => ToDefaultBool(je.GetString()),
                _ => null
            };
        }

        if (value is bool b) return b;

        var s = Convert.ToString(value, CultureInfo.InvariantCulture);

        if (s == "1") return true;
        if (s == "0") return false;

        return bool.TryParse(s, out var parsed)
            ? parsed
            : null;
    }

}
