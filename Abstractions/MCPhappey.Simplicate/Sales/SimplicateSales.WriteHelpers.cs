using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
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

namespace MCPhappey.Simplicate.Sales;

public static partial class SimplicateSales
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static bool CanElicit(RequestContext<CallToolRequestParams> context)
    {
        var capabilities = context.Server.ClientCapabilities
            ?? context.Params?.Meta?["io.modelcontextprotocol/clientCapabilities"]?
                .Deserialize<ClientCapabilities>(JsonSerializerOptions.Web);
        return context.Server.IsMrtrSupported && capabilities?.Elicitation?.Form is not null;
    }

    private static async Task<CallToolResult?> WriteSalesAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string? salesId, SimplicateNewSales incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateSales(incoming, requireSubject: salesId is null);
        var path = "/sales/sales";
        JsonObject? existing = null;
        if (salesId is not null)
        {
            ValidateReference(salesId, nameof(salesId));
            path += "/" + Uri.EscapeDataString(salesId);
            existing = await ReadExistingAsync(services, context, path, ct);
        }

        var dto = incoming;
        Dictionary<string, JsonElement>? answers = null;
        List<JsonObject> customFields = [];
        if (CanElicit(context))
        {
            dto = MergeSeed(MapSalesReadToForm(existing), incoming);
            var overrides = await BuildSalesOverridesAsync(services, context, dto, ct);
            var request = ElicitFormExtensions.CreateElicitRequestParamsForType(dto, overrides);
            var defaults = FormDefaults(dto);
            customFields = await ReadLookupAsync(services, context, "/sales/salescustomfields", "", ct);
            AddCustomFieldSchemas(request, defaults, customFields, existing);
            (answers, _) = await context.TryElicitForm(request, defaults, ct);
            dto = MergeSeed(dto, answers.MapToObject<SimplicateNewSales>());
            if (answers.TryGetValue("team_ids", out var teams) && teams.ValueKind == JsonValueKind.Array)
                dto.TeamIds = string.Join(",", teams.EnumerateArray().Select(team => team.GetString()));
        }

        ValidateSales(dto, requireSubject: salesId is null || CanElicit(context));
        var body = MapSalesWriteBody(dto, existing);
        if (answers is not null)
        {
            ApplyCustomFieldAnswers(body, answers, customFields, existing);
            await ValidateContactsAsync(services, context, dto, ct);
        }
        else if (incoming.ContactId is not null || incoming.InvoiceRecipientContactId is not null)
        {
            // Validation is not name-to-ID resolution; only fetch exact supplied IDs.
            await ValidateContactsAsync(services, context, MergeSeed(MapSalesReadToForm(existing), incoming), ct);
        }
        return await SendWriteAsync(services, context, path, body, salesId is not null, ct);
    }

    private static async Task<CallToolResult?> WriteServiceAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string? serviceId, SimplicateSalesServiceWrite incoming, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateService(incoming, serviceId is null);
        var path = "/sales/service";
        var dto = incoming;
        if (serviceId is not null)
        {
            ValidateReference(serviceId, nameof(serviceId));
            path += "/" + Uri.EscapeDataString(serviceId);
            var existing = await ReadExistingAsync(services, context, path, ct);
            if (CanElicit(context))
                dto = MergeSeed(existing.Deserialize<SimplicateSalesServiceWrite>(WriteOptions)!, incoming);
        }
        if (CanElicit(context))
        {
            var overrides = await BuildServiceOverridesAsync(services, context, dto, ct);
            var elicited = context.Elicit(dto, overrides);
            dto = MergeSeed(dto, elicited);
        }
        ValidateService(dto, serviceId is null || CanElicit(context));
        // Serializing only the writable form intentionally excludes hour_types, cost_types and GET-only fields.
        var body = JsonSerializer.SerializeToNode(dto, WriteOptions)!.AsObject();
        return await SendWriteAsync(services, context, path, body, serviceId is not null, ct);
    }

    private static async Task<JsonObject> ReadExistingAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, string path, CancellationToken ct)
    {
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var result = await services.GetRequiredService<DownloadService>()
            .GetSimplicateItemAsync<JsonObject>(services, context.Server, url, ct);
        if (result?.Data is null || result.Errors?.Any() == true)
            throw new InvalidOperationException("Cannot update: the existing Simplicate record could not be read.");
        return result.Data;
    }

    private static async Task<CallToolResult?> SendWriteAsync(
        IServiceProvider services, RequestContext<CallToolRequestParams> context,
        string path, JsonObject body, bool update, CancellationToken ct)
    {
        if (body.Count == 0)
            return "No writable values were supplied; no mutation performed.".ToTextCallToolResponse();
        ct.ThrowIfCancellationRequested();
        var scraper = services.GetServices<IContentScraper>().OfType<SimplicateScraper>().First();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(path);
        var content = update
            ? await scraper.PutSimplicateItemAsync(services, url, body, ct)
            : await scraper.PostSimplicateItemAsync(services, url, body, context, ct);
        return content?.ToCallToolResult();
    }

    internal static T MergeSeed<T>(T existing, T incoming) where T : class
    {
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(incoming);
            if (value is not null)
                property.SetValue(existing, value);
        }
        return existing;
    }

    private static Dictionary<string, object?> FormDefaults<T>(T dto)
        => typeof(T).GetProperties().ToDictionary(property => property.GetJsonPropertyName(), property => property.GetValue(dto));

    internal static SimplicateNewSales MapSalesReadToForm(JsonObject? existing)
    {
        if (existing is null) return new();
        var dto = existing.Deserialize<SimplicateNewSales>(WriteOptions)!;
        dto.OrganizationId ??= Text(existing["organization"], "id");
        dto.PersonId ??= Text(existing["person"], "id");
        dto.ResponsibleEmployeeId = Text(existing["responsible_employee"], "id");
        dto.ProgressId = Text(existing["progress"], "id");
        dto.SourceId = Text(existing["source"], "id");
        dto.StatusId = Text(existing["status"], "id");
        dto.ReasonId = Text(existing["reason"], "id");
        dto.LostToCompetitorId = Text(existing["lost_to_competitor"], "id");
        if (existing["teams"] is JsonArray teams)
            dto.TeamIds = string.Join(",", teams.Select(team => Text(team, "id")).Where(id => id is not null));
        if (existing["contact"] is JsonObject contact)
        {
            dto.ContactIsActive = contact["is_active"]?.GetValue<bool>();
            dto.ContactWorkFunction = Text(contact, "work_function");
            dto.ContactWorkEmail = Text(contact, "work_email");
            dto.ContactWorkPhone = Text(contact, "work_phone");
            dto.ContactWorkMobile = Text(contact, "work_mobile");
            dto.ContactPersonId = Text(contact, "person_id");
        }
        if (existing["separate_invoice_recipient"] is JsonObject recipient)
        {
            dto.InvoiceRecipientIsSeparate = recipient["is_separate_invoice_recipient"]?.GetValue<bool>();
            dto.InvoiceRecipientOrganizationId = Text(recipient["organization"], "id");
            dto.InvoiceRecipientPersonId = Text(recipient["person"], "id");
            dto.InvoiceRecipientContactId = Text(recipient["contact"], "id");
        }
        return dto;
    }

    internal static JsonObject MapSalesWriteBody(SimplicateNewSales dto, JsonObject? existing)
    {
        var body = JsonSerializer.SerializeToNode(dto, WriteOptions)!.AsObject();
        MapNested(body, "reason", existing?["reason"], [("reason_id", "id")], ["id", "name", "blocked"]);
        MapNested(body, "lost_to_competitor", existing?["lost_to_competitor"], [("lost_to_competitor_id", "id")], ["id", "name"]);
        MapNested(body, "contact", existing?["contact"],
            [("contact_is_active", "is_active"), ("contact_work_function", "work_function"),
             ("contact_work_email", "work_email"), ("contact_work_phone", "work_phone"),
             ("contact_work_mobile", "work_mobile"), ("contact_person_id", "person_id")],
            ["is_active", "work_function", "work_email", "work_phone", "work_mobile", "person_id"]);
        var oldRecipient = existing?["separate_invoice_recipient"];
        var recipient = new JsonObject();
        if (oldRecipient is JsonObject)
        {
            recipient["is_separate_invoice_recipient"] = oldRecipient["is_separate_invoice_recipient"]?.DeepClone();
            foreach (var field in new[] { "organization", "person", "contact" })
            {
                if (Text(oldRecipient[field], "id") is string id)
                    recipient[field + "_id"] = id;
            }
        }
        MapNested(body, "separate_invoice_recipient", recipient,
            [("invoice_recipient_is_separate", "is_separate_invoice_recipient"),
             ("invoice_recipient_organization_id", "organization_id"), ("invoice_recipient_person_id", "person_id"),
             ("invoice_recipient_contact_id", "contact_id")],
            ["is_separate_invoice_recipient", "organization_id", "person_id", "contact_id"]);
        body.Remove("team_ids");
        if (dto.TeamIds is not null)
        {
            var previous = (existing?["teams"] as JsonArray)?.Select(team => Text(team, "id")!).Where(id => id is not null);
            var assignments = SplitTeamIds(dto.TeamIds).BuildSimplicateTeamAssignments(previous);
            if (assignments is not null) body["teams"] = JsonSerializer.SerializeToNode(assignments);
        }
        return body;
    }

    private static void MapNested(JsonObject body, string target, JsonNode? previous,
        (string flat, string nested)[] fields, string[] writable)
    {
        var changes = fields.Where(field => body.ContainsKey(field.flat)).ToArray();
        if (changes.Length == 0) return;
        // A changed reference must not carry a stale display name from the old record.
        var referenceChanged = changes.Any(field => field.nested == "id"
            && Text(body, field.flat) != Text(previous, "id"));
        var nested = new JsonObject();
        if (!referenceChanged && previous is JsonObject old)
            foreach (var key in writable)
                if (old.ContainsKey(key)) nested[key] = old[key]?.DeepClone();
        foreach (var field in changes)
        {
            nested[field.nested] = body[field.flat]?.DeepClone();
            body.Remove(field.flat);
        }
        body[target] = nested;
    }

    internal static string[] SplitTeamIds(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();

    internal static string? Text(JsonNode? node, string key)
        => node is JsonObject obj && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static void ValidateReference(string? value, string field)
    {
        if (value is null) return;
        // Require API reference syntax rather than attempting to resolve a human-readable name.
        var colon = value.IndexOf(':');
        if (colon <= 0 || colon == value.Length - 1 || value.Any(char.IsWhiteSpace)
            || value.Any(character => character is '/' or '\\' or '?' or '#' or ','))
            throw new ValidationException($"{field} must be a Simplicate reference ID (prefix:identifier), not a display name.");
    }

    private static void ValidateReferences<T>(T dto)
    {
        foreach (var property in typeof(T).GetProperties().Where(property => property.Name.EndsWith("Id", StringComparison.Ordinal)))
        {
            var value = property.GetValue(dto) as string;
            ValidateReference(value, property.Name);
        }
    }

    internal static void ValidateSales(SimplicateNewSales dto, bool requireSubject)
    {
        ValidateReferences(dto);
        if ((requireSubject || dto.Subject is not null) && string.IsNullOrWhiteSpace(dto.Subject))
            throw new ValidationException("Sales subject must not be empty.");
        if (dto.ChanceToScore is < 0 or > 100)
            throw new ValidationException("Chance to score must be an integer from 0 to 100.");
        foreach (var date in new[] { dto.StartDate, dto.ExpectedClosingDate })
            if (date is not null && !DateTime.TryParseExact(date,
                ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new ValidationException("Dates must use yyyy-MM-dd or yyyy-MM-dd HH:mm:ss.");
        if (dto.TeamIds is not null)
            foreach (var id in SplitTeamIds(dto.TeamIds)) ValidateReference(id, "teamIds");
        if (dto.ContactWorkEmail is not null && dto.ContactWorkEmail.Length > 0
            && !new EmailAddressAttribute().IsValid(dto.ContactWorkEmail))
            throw new ValidationException("Contact work email is invalid.");
    }

    internal static void ValidateService(SimplicateSalesServiceWrite dto, bool requireFields)
    {
        ValidateReferences(dto);
        if ((requireFields || dto.Name is not null) && string.IsNullOrWhiteSpace(dto.Name))
            throw new ValidationException("Service name must not be empty.");
        if (requireFields && dto.SalesId is null) throw new ValidationException("Sales ID is required.");
        if (dto.InvoiceMethod is not null && dto.InvoiceMethod != "FixedFee")
            throw new ValidationException("Only invoice method FixedFee is documented.");
        if (dto.SubscriptionCycle is not null && !new[] { "Month", "Quarter", "Half_a_year", "Year" }.Contains(dto.SubscriptionCycle))
            throw new ValidationException("Invalid subscription cycle.");
    }

    private static async Task ValidateContactsAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, SimplicateNewSales dto, CancellationToken ct)
    {
        foreach (var (id, organization, person) in new[]
        {
            (dto.ContactId, dto.OrganizationId, dto.PersonId),
            (dto.InvoiceRecipientContactId, dto.InvoiceRecipientOrganizationId, dto.InvoiceRecipientPersonId)
        })
        {
            if (id is null) continue;
            var contact = await ReadExistingAsync(services, context, "/crm/contactperson/" + Uri.EscapeDataString(id), ct);
            if (organization is not null && Text(contact["organization"], "id") != organization
                || person is not null && (Text(contact, "person_id") ?? Text(contact["person"], "id")) != person)
                throw new ValidationException("The supplied contact ID does not belong to the selected organization/person.");
        }
    }
}
