using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Projects;

public static partial class SimplicateProjects
{
    [Description("Set one project custom field using its definition name and a scalar value. Numeric values use invariant notation; dropdown values are option IDs. Supported clients review a typed form. Other fields are preserved. Clearing and undocumented control types are not supported.")]
    [McpServerTool(OpenWorld = false, Destructive = true, Title = "Set project custom field in Simplicate")]
    public static async Task<CallToolResult?> SimplicateProjects_SetProjectCustomField(
        [Description("Exact project ID.")] string projectId,
        [Description("Custom field definition name, not its label.")] string fieldName,
        [Description("Scalar field value; invariant numeric text, documented date/time text, or dropdown option ID. Not JSON.")] string value,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
    {
        ValidateId(projectId, "projectId", true);
        if (string.IsNullOrWhiteSpace(fieldName)) throw new ValidationException("Custom field name is required.");
        var key = "custom_field:" + fieldName;
        Dictionary<string, JsonElement>? answers = null;
        var resumed = requestContext.Params?.InputResponses?.ContainsKey("elicitForm") == true;
        if (resumed)
            (answers, _) = await requestContext.TryElicitForm(
                new ElicitRequestParams { Message = "Review custom field value." }, cancellationToken: cancellationToken);
        var path = "/projects/project/" + Uri.EscapeDataString(projectId);
        var existing = await ReadExistingAsync(serviceProvider, requestContext, path, cancellationToken);
        if (existing["custom_fields"] is not JsonArray)
            throw new ValidationException("Cannot safely edit custom fields: the existing collection is unavailable.");

        if (!resumed && requestContext.SupportsFormElicitation())
        {
            var definitions = await ReadLookupAsync(serviceProvider, requestContext, "/projects/projectcustomfields", cancellationToken);
            var definition = definitions.SingleOrDefault(item => Text(item, "name") == fieldName)
                ?? throw new ValidationException("The custom field definition was not found.");
            var request = new ElicitRequestParams
            {
                Message = $"Review custom field '{Text(definition, "label") ?? fieldName}'.",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(), Required = []
                }
            };
            var defaults = new Dictionary<string, object?>();
            var seed = existing.DeepClone().AsObject();
            var fields = seed["custom_fields"]!.AsArray();
            var old = fields.OfType<JsonObject>().FirstOrDefault(field => Text(field, "name") == fieldName);
            if (old is not null) old["value"] = value;
            else fields.Add(new JsonObject { ["name"] = fieldName, ["value"] = value });
            Sales.SimplicateSales.AddCustomFieldSchemas(request, defaults, [definition], seed);
            if (!request.RequestedSchema.Properties.ContainsKey(key))
                throw new ValidationException("This custom field control/type is not documented sufficiently for safe editing.");
            if (!request.RequestedSchema.Required.Contains(key)) request.RequestedSchema.Required.Add(key);
            if (Text(definition, "render_type") != "dropdown")
            {
                if (Text(definition, "value_type") == "Integer")
                    defaults[key] = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                        ? integer : throw new ValidationException("The custom field requires an integer.");
                else if (Text(definition, "value_type") == "Decimal")
                    defaults[key] = decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number) ? number : throw new ValidationException("The custom field requires a decimal.");
            }
            (answers, _) = await requestContext.TryElicitForm(request, defaults, cancellationToken);
        }
        // Direct inputs use the documented scalar string encoding; Simplicate validates the value.
        answers ??= new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) };
        if (!answers.TryGetValue(key, out var answer) || answer.ValueKind == JsonValueKind.Null
            || answer.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(answer.GetString()))
            throw new ValidationException("A nonempty value is required; custom-field clearing semantics are not documented.");
        var body = new JsonObject();
        // This setter edits exactly one field, regardless of any other submitted form properties.
        Sales.SimplicateSales.ApplySubmittedCustomFields(body, new() { [key] = answer }, existing);
        return await SendWriteAsync(serviceProvider, requestContext, path, body, true, cancellationToken);
    }
}
