using System.Text.Json.Nodes;
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
    private static async Task<List<JsonObject>> ReadLookupAsync(IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string endpoint, CancellationToken ct)
    {
        var url = services.GetRequiredService<SimplicateOptions>().GetApiUrl(endpoint);
        var download = services.GetRequiredService<DownloadService>();
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

    private static string Label(JsonObject item)
        => Text(item, "full_name") ?? Text(item, "name") ?? Text(item, "label") ?? Text(item, "id")!;

    // This method is called only while building the initial form, never on resume or after a write.
    private static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>> BuildOverridesAsync<T>(
        IServiceProvider services, RequestContext<CallToolRequestParams> context, T dto, CancellationToken ct)
    {
        var overrides = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();
        var tables = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (var property in typeof(T).GetProperties())
        {
            var key = property.GetJsonPropertyName();
            var current = property.GetValue(dto) as string;
            string[]? choices = key == "source" ? ["schedule", "timer", "timesheet"]
                : key == "type" && typeof(T) == typeof(SimplicateHoursTypeWrite) ? ["Work Type"]
                : key == "type" && typeof(T) == typeof(SimplicateTimesheetRowWrite) ? ["hours", "costs", "mileage"] : null;
            if (choices is not null)
            {
                overrides[key] = Sales.SimplicateSales.LookupSchema(property.Name, current,
                    choices.Select(value => new JsonObject { ["id"] = value, ["label"] = value }), Label);
                continue;
            }
            var endpoint = key switch
            {
                "employee_id" => "/hrm/employee",
                "project_id" => "/projects/project",
                "projectservice_id" or "project_service_id" => "/projects/service",
                "type_id" => "/hours/hourstype",
                "approvalstatus_id" => "/hours/approvalstatus",
                "absence_type_id" => "/hrm/absencetype",
                "leave_type_id" => "/hrm/leavetype",
                "vatclass_id" => "/invoices/vatclass",
                "itemtype_id" when dto is SimplicateTimesheetRowWrite { Type: "hours" } => "/hours/hourstype",
                _ => null
            };
            if (endpoint is null) continue;
            if (!tables.TryGetValue(endpoint, out var items))
            {
                try { items = await ReadLookupAsync(services, context, endpoint, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (!ct.IsCancellationRequested) { items = []; }
                tables[endpoint] = items;
            }
            var options = items.Where(item => Text(item, "id") is not null && item["blocked"]?.GetValue<bool>() != true).ToList();
            if (options.Count == 0)
            {
                overrides[key] = new ElicitRequestParams.StringSchema
                {
                    Title = property.Name, Description = property.GetDescription(), Default = current
                };
                continue;
            }
            // Services include their project in the label so duplicate service names remain distinguishable.
            overrides[key] = Sales.SimplicateSales.LookupSchema(property.Name, current, options,
                item => endpoint == "/projects/service" && Text(item["project"], "name") is string project
                    ? $"{Label(item)} — {project}" : Label(item));
        }
        return overrides;
    }
}
