using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using MCPhappey.Simplicate.Hours.Models;
using MCPhappey.Simplicate.Options;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Simplicate.Hours;

public static partial class SimplicateHours
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string? Text(JsonNode? node, string key)
        => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static void ValidateId(string? value, string field, bool required = false)
    {
        if (value is null && !required) return;
        var colon = value?.IndexOf(':') ?? -1;
        if (value is null || colon <= 0 || colon == value.Length - 1 || value.Any(char.IsWhiteSpace)
            || value.Any(character => character is '/' or '\\' or '?' or '#' or ','))
            throw new ValidationException($"{field} must be an exact Simplicate ID (prefix:identifier), not a display name.");
    }

    private static string[] RequiredFields<T>() => typeof(T) == typeof(SimplicateHourWrite)
        ? ["hours", "employee_id", "project_id", "projectservice_id", "type_id", "start_date"]
        : typeof(T) == typeof(SimplicateHoursTypeWrite) ? ["label"]
        : typeof(T) == typeof(SimplicateHoursAbsenceWrite) ? ["employee_id", "absence_type_id", "start_date", "end_date"]
        : typeof(T) == typeof(SimplicateHoursLeaveWrite) ? ["employee_id", "leave_type_id", "start_date", "end_date"]
        : typeof(T) == typeof(SimplicateTimesheetRowWrite)
            ? ["employee_id", "start_date", "end_date", "project_id", "project_service_id", "itemtype_id", "type"]
        : ["employee_id", "start_date", "end_date"];

    private static JsonObject SerializeWrite<T>(T dto)
        => JsonSerializer.SerializeToNode(dto, WriteOptions)!.AsObject();

    private static DateTime ParseDate(string value, string field, bool dateOnly)
    {
        string[] formats = dateOnly ? ["yyyy-MM-dd"]
            : ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"];
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ValidationException($"{field} must use {(dateOnly ? "yyyy-MM-dd" : "yyyy-MM-dd or a documented timestamp")}.");
        return date;
    }

    private static void ValidateWrite<T>(JsonObject body, bool create)
    {
        if (create)
            foreach (var key in RequiredFields<T>())
                if (body[key] is null || body[key] is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                    throw new ValidationException($"{key} is required.");

        foreach (var (key, node) in body)
        {
            if (node is not JsonValue value) continue;
            if (value.TryGetValue<string>(out var text))
            {
                if (key.EndsWith("_id", StringComparison.Ordinal) && key != "external_item_id") ValidateId(text, key);
                if (key.EndsWith("_date", StringComparison.Ordinal) || key is "recurrence_rrule_dtstart" or "recurrence_rrule_until")
                    ParseDate(text, key, typeof(T) == typeof(SimplicateHoursSubmissionWrite));
                if (key == "source" && text is not ("schedule" or "timer" or "timesheet"))
                    throw new ValidationException("source must be schedule, timer, or timesheet.");
                if (key == "type" && typeof(T) == typeof(SimplicateHoursTypeWrite) && text != "Work Type")
                    throw new ValidationException("Only hour type category 'Work Type' is documented.");
                if (key == "type" && typeof(T) == typeof(SimplicateTimesheetRowWrite) && text is not ("hours" or "costs" or "mileage"))
                    throw new ValidationException("Timesheet row type must be hours, costs, or mileage.");
                if (key == "label" && string.IsNullOrWhiteSpace(text)) throw new ValidationException("Hour type label must not be empty.");
            }
            if (key is "hours" or "duration_in_minutes" && value.TryGetValue<double>(out var number) && (!double.IsFinite(number) || number < 0))
                throw new ValidationException($"{key} must be a finite nonnegative number.");
        }
        if (Text(body, "start_date") is string start && Text(body, "end_date") is string end
            && ParseDate(end, "end_date", typeof(T) == typeof(SimplicateHoursSubmissionWrite))
                < ParseDate(start, "start_date", typeof(T) == typeof(SimplicateHoursSubmissionWrite)))
            throw new ValidationException("end_date must not precede start_date.");
        if (body["clear_address"]?.GetValue<bool>() == true && body.Any(pair => IsAddressField(pair.Key)))
            throw new ValidationException("Do not combine clear_address=true with address fields.");
    }

    private static bool IsAddressField(string key)
        => key.StartsWith("address_", StringComparison.Ordinal) && key != "address_id";

    private static string[] NestedPath(string key)
        => IsAddressField(key) ? ["address", key[8..]]
        : key.StartsWith("recurrence_rrule_", StringComparison.Ordinal) ? ["recurrence", "rrule", key[17..]]
        : key.StartsWith("recurrence_", StringComparison.Ordinal) ? ["recurrence", key[11..]]
        : key == "external_item_id" ? ["external_item", "id"] : [key];

    private static JsonNode? ReadPath(JsonObject existing, string[] path)
    {
        JsonNode? node = existing;
        foreach (var key in path) node = (node as JsonObject)?[key];
        return node;
    }

    // Read only the writable fields, mapping GET relations and nested objects into primitive form values.
    private static T ReadForm<T>(JsonObject? existing) where T : class, new()
    {
        if (existing is null) return new();
        var fields = new JsonObject();
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            if (key == "clear_address") continue;
            var value = ReadPath(existing, NestedPath(key));
            if (value is not null) fields[key] = value.DeepClone();
            else if (key.EndsWith("_id", StringComparison.Ordinal) && NestedPath(key).Length == 1)
            {
                var relation = key switch
                {
                    "project_service_id" => "projectservice",
                    "absence_type_id" => "absence_type",
                    "leave_type_id" => "leave_type",
                    _ => key[..^3]
                };
                if (Text(existing[relation], "id") is string id) fields[key] = id;
            }
        }
        return fields.Deserialize<T>(WriteOptions) ?? new();
    }

    private static T Merge<T>(T seed, T incoming) where T : class
    {
        foreach (var property in typeof(T).GetProperties())
            if (property.GetValue(incoming) is object value) property.SetValue(seed, value);
        return seed;
    }

    private static JsonObject MapAnswers<T>(Dictionary<string, JsonElement> answers)
    {
        var body = new JsonObject();
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            if (!answers.TryGetValue(key, out var answer) || answer.ValueKind == JsonValueKind.Null) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var valid = type == typeof(string) ? answer.ValueKind == JsonValueKind.String
                : type == typeof(bool) ? answer.ValueKind is JsonValueKind.True or JsonValueKind.False
                : type == typeof(int) ? answer.ValueKind == JsonValueKind.Number && answer.TryGetInt32(out _)
                : answer.ValueKind == JsonValueKind.Number && answer.TryGetDouble(out var number) && double.IsFinite(number);
            if (!valid) throw new ValidationException($"Invalid form value for {key}.");
            body[key] = JsonNode.Parse(answer.GetRawText());
        }
        return body;
    }

    // Preserve unchanged writable siblings when sending a nested object to PUT, never GET-only fields.
    private static JsonObject MapWriteBody<T>(JsonObject changes, JsonObject? existing) where T : class, new()
    {
        var body = new JsonObject();
        var nestedRoots = changes.Select(pair => pair.Key).Select(NestedPath).Where(path => path.Length > 1)
            .Select(path => path[0]).Distinct().ToArray();
        var previous = SerializeWrite(ReadForm<T>(existing));
        foreach (var (key, value) in previous)
            if (NestedPath(key) is var path && path.Length > 1 && nestedRoots.Contains(path[0]))
                SetPath(body, path, value);
        foreach (var (key, value) in changes)
        {
            if (key == "clear_address") continue;
            SetPath(body, NestedPath(key), value);
        }
        if (changes["clear_address"]?.GetValue<bool>() == true) body["address"] = null;
        return body;
    }

    private static void SetPath(JsonObject body, string[] path, JsonNode? value)
    {
        var target = body;
        foreach (var key in path[..^1])
        {
            if (target[key] is not JsonObject nested) target[key] = nested = new JsonObject();
            target = nested;
        }
        target[path[^1]] = value?.DeepClone();
    }

    private static async Task<CallToolResult?> WriteAsync<T>(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, string? id, T incoming,
        CancellationToken ct) where T : class, new()
    {
        ct.ThrowIfCancellationRequested();
        Dictionary<string, JsonElement>? answers = null;
        var resumed = context.Params?.InputResponses?.ContainsKey("elicitForm") == true;
        // Consume declines/cancellation before any HTTP access. A save never re-downloads lookup lists.
        if (resumed)
            (answers, _) = await context.TryElicitForm(new ElicitRequestParams { Message = "Review hours details." }, cancellationToken: ct);
        var supplied = SerializeWrite(incoming);
        ValidateWrite<T>(supplied, false);
        var path = endpoint;
        JsonObject? existing = null;
        if (id is not null)
        {
            ValidateId(id, "recordId", true);
            path += "/" + Uri.EscapeDataString(id);
            var response = await services.GetRequiredService<DownloadService>().GetSimplicateItemAsync<JsonObject>(
                services, context.Server, services.GetRequiredService<SimplicateOptions>().GetApiUrl(path), ct);
            if (response?.Data is null || response.Errors?.Any() == true)
                throw new InvalidOperationException("Cannot update: the existing Simplicate record could not be read.");
            existing = response.Data;
        }
        var seed = Merge(ReadForm<T>(existing), incoming);
        var defaults = SerializeWrite(seed);
        if (!resumed && context.SupportsFormElicitation())
        {
            var overrides = await BuildOverridesAsync(services, context, seed, ct);
            var request = ElicitFormExtensions.CreateElicitRequestParamsForType(seed, overrides);
            if (id is null) foreach (var field in RequiredFields<T>()) request.RequestedSchema!.Required!.Add(field);
            var fallback = typeof(T).GetProperties().ToDictionary(property => property.GetJsonPropertyName(), property => property.GetValue(seed));
            (answers, _) = await context.TryElicitForm(request, fallback, ct);
        }
        var changes = supplied;
        if (answers is not null)
        {
            var form = MapAnswers<T>(answers);
            changes = id is null ? defaults.DeepClone().AsObject() : supplied.DeepClone().AsObject();
            foreach (var (key, value) in form)
                if (id is null || supplied.ContainsKey(key) || !JsonNode.DeepEquals(defaults[key], value))
                    changes[key] = value?.DeepClone();
        }
        ValidateWrite<T>(changes, id is null);
        // Validate the final date range even when only one boundary was changed.
        var effective = defaults.DeepClone().AsObject();
        foreach (var (key, value) in changes) effective[key] = value?.DeepClone();
        // clear_address is a command, not part of the current address state.
        if (changes["clear_address"]?.GetValue<bool>() == true)
            foreach (var key in effective.Select(pair => pair.Key).Where(IsAddressField).ToArray()) effective.Remove(key);
        ValidateWrite<T>(effective, false);
        var body = MapWriteBody<T>(changes, existing);
        if (body.Count == 0) return "No writable changes supplied; no mutation performed.".ToTextCallToolResponse();
        ct.ThrowIfCancellationRequested();
        var scraper = services.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var content = id is null
            ? await scraper.PostSimplicateItemAsync(services, url, body, context, ct)
            : await scraper.PutSimplicateItemAsync(services, url, body, ct);
        return content?.ToCallToolResult();
    }

    private static async Task<CallToolResult> DeleteAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, string id, string success, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateId(id, "recordId", true);
        var key = "confirmDeleteSimplicateHoursRecord";
        if (context.SupportsFormElicitation() || context.Params?.InputResponses?.ContainsKey(key) == true)
        {
            var confirmation = context.Elicit(new ConfirmDeleteSimplicateHoursRecord(), message: id);
            if (!string.Equals(confirmation.Name?.Trim(), id, StringComparison.Ordinal))
                return "Confirmation must match the exact record ID.".ToErrorCallToolResponse();
        }
        ct.ThrowIfCancellationRequested();
        return (await services.DeleteSimplicateResourceAsync(endpoint + "/" + Uri.EscapeDataString(id), success, ct))!;
    }
}
