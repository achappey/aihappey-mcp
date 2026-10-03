using System.Text.Json.Nodes;
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
    private static async Task<List<JsonObject>> ReadLookupAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, CancellationToken ct)
    {
        var download = services.GetRequiredService<DownloadService>();
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(endpoint);
        var items = new List<JsonObject>();
        for (var offset = 0; ; offset += 100)
        {
            ct.ThrowIfCancellationRequested();
            var page = await download.GetSimplicatePageAsync<JsonObject>(services, context.Server,
                $"{url}?limit=100&offset={offset}&metadata=count", ct);
            if (page?.Data is null)
                throw new InvalidOperationException($"Cannot read Simplicate lookup {endpoint}.");
            var batch = page.Data.ToList();
            items.AddRange(batch);
            if (batch.Count < 100 || page.Metadata?.Count > 0 && items.Count >= page.Metadata.Count) break;
        }
        return items;
    }

    private static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>> BuildOverridesAsync<T>(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, T dto, CancellationToken ct)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        // A failed optional dropdown does not prevent exact-ID input. Cancellation is never swallowed.
        var tables = new Dictionary<string, List<JsonObject>>();
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            var current = property.GetValue(dto) as string;
            var endpoint = key switch
            {
                "project_manager_id" or "employee_id" or "employee_ids" => "/hrm/employee",
                "organization_id" or "invoice_recipient_organization_id" => "/crm/organization",
                "person_id" or "invoice_recipient_person_id" => "/crm/person",
                "contact_id" or "invoice_recipient_contact_id" => "/crm/contactperson",
                "my_organization_profile_id" => "/crm/myorganizationprofile",
                "project_status_id" => "/projects/projectstatus",
                "status_id" => "/projects/assignmentstatus",
                "vat_class_id" => "/invoices/vatclass",
                "revenue_group_id" => "/sales/revenuegroup",
                "divergent_payment_term_id" => "/invoices/paymentterm",
                "default_service_id" => "/services/defaultservice",
                "team_ids" or "team_id" => "/hrm/team",
                _ => null
            };
            if (key is "invoice_method" or "hours_type")
            {
                var choices = key == "invoice_method" ? new[] { "FixedFee" } : ["per_week", "total"];
                // Existing read-only invoice-method values can be displayed and left unchanged.
                overrides[key] = Sales.SimplicateSales.LookupSchema(property.Name, current,
                    choices.Select(value => new JsonObject { ["id"] = value, ["label"] = value }), item => Text(item, "label"));
            }
            if (endpoint is null) continue;
            if (!tables.TryGetValue(endpoint, out var items))
            {
                try { items = await ReadLookupAsync(services, context, endpoint, ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { items = []; }
                tables[endpoint] = items;
            }
            var options = items.Where(item => Text(item, "id") is not null && item["blocked"]?.GetValue<bool>() != true)
                .GroupBy(item => Text(item, "id"), StringComparer.Ordinal).Select(group => group.First()).ToList();
            if (key == "divergent_payment_term_id")
                options.Insert(0, new JsonObject { ["id"] = "null", ["label"] = "Use the default payment term (unset override)" });
            if (key is "team_ids" or "employee_ids")
            {
                var ids = current is null ? [] : SplitIds(current);
                foreach (var id in ids)
                    if (options.All(item => Text(item, "id") != id)) options.Add(new JsonObject { ["id"] = id, ["label"] = $"Current value ({id})" });
                if (options.Count > 0)
                    overrides[key] = new ElicitRequestParams.TitledMultiSelectEnumSchema
                    {
                        Title = property.Name, Description = property.GetDescription(), Default = ids,
                        Items = new ElicitRequestParams.TitledEnumItemsSchema
                        {
                            AnyOf = [.. options.OrderBy(Label, StringComparer.OrdinalIgnoreCase).Select(item =>
                                new ElicitRequestParams.EnumSchemaOption { Const = Text(item, "id")!, Title = Label(item) })]
                        }
                    };
            }
            else overrides[key] = Sales.SimplicateSales.LookupSchema(property.Name, current, options, Label);
        }
        return overrides;
    }

    private static string Label(JsonObject item)
        => Text(item, "full_name") ?? Text(item, "name") ?? Text(item, "label")
            ?? (Text(item["person"], "full_name") is string person
                ? $"{person} — {Text(item["organization"], "name") ?? Text(item, "id")}" : Text(item, "id"))!;
}
