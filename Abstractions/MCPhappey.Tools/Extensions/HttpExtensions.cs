using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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

    public static ElicitRequestParams.PrimitiveSchemaDefinition? ToElicitSchemaDef(this ColumnDefinition col, dynamic? defaultValue = null)
    {
        var title = col.DisplayName ?? col.Name;
        var desc = col.Description ?? "";

        if (col.Text != null)
        {
            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = defaultValue
            };
        }

        if (col.Number != null)
        {
            return new ElicitRequestParams.NumberSchema
            {
                Title = title,
                Description = desc,
                Minimum = col.Number?.Minimum,
                Maximum = col.Number?.Maximum,
                Default = defaultValue is double doubleVal ? doubleVal
                    : defaultValue is not null ? double.Parse(defaultValue) : null
            };
        }

        if (col.Choice != null)
        {
            return new ElicitRequestParams.EnumSchema
            {
                Title = title,
                Description = desc,
                Enum = col.Choice.Choices?.ToArray() ?? [],
                Default = defaultValue,
                EnumNames = null // Could map friendly names if present
            };
        }


        if (col.DateTime != null)
        {
            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = defaultValue,
                Format = "date-time"
            };
        }


        if (col.Boolean != null)
        {
            return new ElicitRequestParams.BooleanSchema
            {
                Title = title,
                Description = desc,
                Default = col.DefaultValue?.Value == null ? defaultValue : col.DefaultValue.Value == "1"
            };
        }

        if (col.HyperlinkOrPicture != null)
        {
            return new ElicitRequestParams.StringSchema
            {
                Title = title,
                Description = desc,
                Default = defaultValue?.ToString(),
                Format = "uri"
            };
        }

        return null;
    }
}
