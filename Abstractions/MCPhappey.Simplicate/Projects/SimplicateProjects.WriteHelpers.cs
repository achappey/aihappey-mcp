using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Projects;

public static partial class SimplicateProjects
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? Text(JsonNode? node, string key)
        => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string[] SplitIds(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();

    private static void ValidateId(string? value, string field, bool required = false)
    {
        if (value is null && !required) return;
        var colon = value?.IndexOf(':') ?? -1;
        if (value is null || colon <= 0 || colon == value.Length - 1 || value.Any(char.IsWhiteSpace)
            || value.Any(character => character is '/' or '\\' or '?' or '#' or ','))
            throw new ValidationException($"{field} must be an exact Simplicate ID (prefix:identifier), not a display name.");
    }

    private static void ValidateWrite<T>(T dto)
    {
        foreach (var property in typeof(T).GetProperties())
        {
            var value = property.GetValue(dto);
            if (value is not string text) continue;
            var key = property.GetJsonPropertyName();
            if (key.EndsWith("_id", StringComparison.Ordinal))
            {
                if (key == "divergent_payment_term_id" && text == "null") continue;
                // External identifiers are arbitrary caller-owned strings, not Simplicate references.
                if (key != "external_id") ValidateId(text, key);
            }
            if (key is "team_ids" or "employee_ids")
            {
                if (key == "employee_ids" && SplitIds(text).Length == 0)
                    throw new ValidationException("Empty employee selection is not supported without documented clearing semantics.");
                foreach (var id in SplitIds(text)) ValidateId(id, key);
            }
            if (key.EndsWith("_date", StringComparison.Ordinal) || key is "created_at" or "updated_at")
                if (!DateTime.TryParseExact(text, ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ssK"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new ValidationException($"{key} must be yyyy-MM-dd or a documented timestamp.");
            if (key == "name" && string.IsNullOrWhiteSpace(text))
                throw new ValidationException("Name must not be empty.");
            if (key == "invoice_method" && text != "FixedFee")
                throw new ValidationException("Only FixedFee is documented for service mutations.");
            if (key == "hours_type" && text is not ("per_week" or "total"))
                throw new ValidationException("hours_type must be per_week or total.");
        }
    }

    private static JsonObject SerializeWrite<T>(T dto)
        => JsonSerializer.SerializeToNode(dto, WriteOptions)!.AsObject();

    private static T ReadForm<T>(JsonObject? existing) where T : class, new()
    {
        if (existing is null) return new();
        var writable = new JsonObject();
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            if (existing.ContainsKey(key)) writable[key] = existing[key]?.DeepClone();
            else if (key.EndsWith("_id", StringComparison.Ordinal))
            {
                var relation = key[..^3];
                if (Text(existing[relation], "id") is string id) writable[key] = id;
            }
        }
        if (typeof(T) == typeof(SimplicateNewProject))
        {
            var recipient = existing["separate_invoice_recipient"];
            if (recipient is JsonObject)
            {
                writable["invoice_recipient_is_separate"] = recipient["is_separate_invoice_recipient"]?.DeepClone();
                foreach (var key in new[] { "organization", "person", "contact" })
                    writable["invoice_recipient_" + key + "_id"] = Text(recipient, key + "_id") ?? Text(recipient[key], "id");
            }
            if (existing["teams"] is JsonArray teams)
                writable["team_ids"] = string.Join(',', teams.OfType<JsonObject>()
                    .Where(team => team["value"]?.GetValue<bool>() != false).Select(team => Text(team, "id")).OfType<string>());
        }
        if (existing["employees"] is JsonArray employees && typeof(SimplicateAssignmentWrite).IsAssignableFrom(typeof(T)))
            writable["employee_ids"] = string.Join(',', employees.Select(employee => Text(employee, "id")).OfType<string>());
        return writable.Deserialize<T>(WriteOptions) ?? new();
    }

    private static T Merge<T>(T seed, T incoming) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
            if (property.GetValue(incoming) is object value) property.SetValue(seed, value);
        return seed;
    }

    private static async Task<JsonObject> ReadExistingAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string path, CancellationToken ct)
    {
        var response = await services.GetRequiredService<DownloadService>().GetSimplicateItemAsync<JsonObject>(
            services, context.Server, services.GetRequiredService<SimplicateOptions>().GetApiUrl(path), ct);
        if (response?.Data is null || response.Errors?.Any() == true)
            throw new InvalidOperationException("Cannot update: the existing Simplicate record could not be read.");
        return response.Data;
    }

    private static async Task<CallToolResult?> SendWriteAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string path, JsonObject body, bool update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (body.Count == 0) return "No writable changes supplied; no mutation performed.".ToTextCallToolResponse();
        var scraper = services.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var content = update
            ? await scraper.PutSimplicateItemAsync(services, url, body, ct)
            : await scraper.PostSimplicateItemAsync(services, url, body, context, ct);
        return content?.ToCallToolResult();
    }

    // Forms prefill the current record, but PUT contains only supplied values or actual form changes.
    // This also avoids resubmitting existing Hours/Subscription invoice methods to FixedFee-only writes.
    internal static JsonObject ChangedValues(JsonObject supplied, JsonObject defaults, JsonObject answers)
    {
        var changes = supplied.DeepClone().AsObject();
        foreach (var (key, value) in answers)
            if (value is not null && (supplied.ContainsKey(key) || !JsonNode.DeepEquals(defaults[key], value)))
                changes[key] = value.DeepClone();
        return changes;
    }

    private static async Task<CallToolResult?> WriteAsync<T>(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, string? id, T incoming,
        CancellationToken ct) where T : class, new()
    {
        ct.ThrowIfCancellationRequested();
        ValidateWrite(incoming);
        Dictionary<string, JsonElement>? answers = null;
        var resumed = context.Params?.InputResponses?.ContainsKey("elicitForm") == true;
        if (resumed)
            (answers, _) = await context.TryElicitForm(new ElicitRequestParams { Message = "Review project details." }, cancellationToken: ct);
        var path = endpoint;
        JsonObject? existing = null;
        if (id is not null)
        {
            ValidateId(id, "recordId", true);
            path += "/" + Uri.EscapeDataString(id);
            existing = await ReadExistingAsync(services, context, path, ct);
        }
        var supplied = SerializeWrite(incoming);
        var seed = Merge(ReadForm<T>(existing), incoming);
        var defaults = SerializeWrite(seed);
        var body = supplied;
        if (!resumed && context.SupportsFormElicitation())
        {
            var overrides = await BuildOverridesAsync(services, context, seed, ct);
            var request = ElicitFormExtensions.CreateElicitRequestParamsForType(seed, overrides);
            var schema = request.RequestedSchema!;
            if (id is null)
            {
                schema.Required.Add("name");
                if (incoming is SimplicateNewProject) schema.Required.Add("project_manager_id");
                if (incoming is SimplicateNewProjectService) schema.Required.Add("project_id");
                if (incoming is SimplicateNewAssignment) schema.Required.Add("projecthourstype_id");
            }
            var fallback = typeof(T).GetProperties().ToDictionary(p => p.GetJsonPropertyName(), p => p.GetValue(seed));
            if (incoming is SimplicateNewProject)
            {
                var definitions = await ReadLookupAsync(services, context, "/projects/projectcustomfields", ct);
                Sales.SimplicateSales.AddCustomFieldSchemas(request, fallback, definitions, existing);
            }
            (answers, _) = await context.TryElicitForm(request, fallback, ct);
        }
        if (answers is not null)
        {
            var formBody = MapFormAnswers<T>(answers);
            body = id is null ? ChangedValues(defaults, defaults, formBody) : ChangedValues(supplied, defaults, formBody);
        }
        var changed = body.Deserialize<T>(WriteOptions) ?? new();
        ValidateWrite(changed);
        if (id is null)
        {
            if (string.IsNullOrWhiteSpace(Text(body, "name"))) throw new ValidationException("Name is required.");
            if (incoming is SimplicateNewProject) ValidateId(Text(body, "project_manager_id"), "projectManagerId", true);
            if (incoming is SimplicateNewProjectService) ValidateId(Text(body, "project_id"), "projectId", true);
            if (incoming is SimplicateNewAssignment) ValidateId(Text(body, "projecthourstype_id"), "projectHoursTypeId", true);
        }
        if (incoming is SimplicateNewProject)
        {
            MapProjectBody(body, existing);
            if (answers is not null)
            {
                if (existing is not null && existing["custom_fields"] is not JsonArray
                    && answers.Any(answer => answer.Key.StartsWith("custom_field:", StringComparison.Ordinal)
                        && answer.Value.ValueKind != JsonValueKind.Null))
                    throw new ValidationException("Cannot safely edit custom fields: the existing collection is unavailable.");
                Sales.SimplicateSales.ApplySubmittedCustomFields(body, answers, existing);
            }
        }
        if (body.Remove("employee_ids", out var employees) && employees is not null)
            body["employees"] = new JsonArray([.. SplitIds(employees.GetValue<string>())
                .Select(employee => (JsonNode)new JsonObject { ["id"] = employee })]);
        return await SendWriteAsync(services, context, path, body, id is not null, ct);
    }

    private static JsonObject MapFormAnswers<T>(Dictionary<string, JsonElement> answers)
    {
        var result = new JsonObject();
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            if (!answers.TryGetValue(key, out var answer) || answer.ValueKind == JsonValueKind.Null) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (key is "team_ids" or "employee_ids" && answer.ValueKind == JsonValueKind.Array)
            {
                if (answer.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                    throw new ValidationException($"{key} must contain ID strings.");
                result[key] = string.Join(',', answer.EnumerateArray().Select(item => item.GetString()));
            }
            else
            {
                var valid = type == typeof(string) ? answer.ValueKind == JsonValueKind.String
                    : type == typeof(bool) ? answer.ValueKind is JsonValueKind.True or JsonValueKind.False
                    : answer.ValueKind == JsonValueKind.Number && answer.TryGetDecimal(out _);
                if (!valid) throw new ValidationException($"Invalid form value for {key}.");
                result[key] = JsonNode.Parse(answer.GetRawText());
            }
        }
        return result;
    }

    internal static void MapProjectBody(JsonObject body, JsonObject? existing)
    {
        var fields = new[] { ("invoice_recipient_is_separate", "is_separate_invoice_recipient"),
            ("invoice_recipient_organization_id", "organization_id"), ("invoice_recipient_person_id", "person_id"),
            ("invoice_recipient_contact_id", "contact_id") };
        if (fields.Any(field => body.ContainsKey(field.Item1)))
        {
            var recipient = new JsonObject();
            var previous = existing?["separate_invoice_recipient"];
            if (previous is JsonObject)
            {
                if (previous["is_separate_invoice_recipient"] is JsonNode separate)
                    recipient["is_separate_invoice_recipient"] = separate.DeepClone();
                foreach (var key in new[] { "organization", "person", "contact" })
                    if ((Text(previous, key + "_id") ?? Text(previous[key], "id")) is string reference)
                        recipient[key + "_id"] = reference;
            }
            foreach (var (flat, nested) in fields)
                if (body.Remove(flat, out var value)) recipient[nested] = value;
            body["separate_invoice_recipient"] = recipient;
        }
        if (body.Remove("team_ids", out var teams) && teams is not null)
        {
            // Match CRM: existing teams are optional. Supplied IDs are explicit assignments;
            // removals are emitted only for known existing IDs. An empty selection with
            // no existing collection emits no team payload, leaving other updates intact.
            var previous = (existing?["teams"] as JsonArray)?.OfType<JsonObject>()
                .Where(team => team["value"]?.GetValue<bool>() != false).Select(team => Text(team, "id")).OfType<string>();
            var assignments = SplitIds(teams.GetValue<string>()).BuildSimplicateTeamAssignments(previous);
            if (assignments is not null) body["teams"] = JsonSerializer.SerializeToNode(assignments);
        }
    }

}
