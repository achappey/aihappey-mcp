using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Mime;

namespace MCPhappey.Tools.Dataverse;

public static class DataversePluginExtensions
{

    public static readonly string API_URL = "/api/data/v9.2/";

    private static readonly ConcurrentDictionary<string, string> _entitySetCache = new();

    private static async Task<string> GetEntitySetAsync(
            HttpClient httpClient, string host,
            string entityLogicalName, CancellationToken ct)
    {
        if (_entitySetCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var json = await httpClient.GetStringAsync(
            $"https://{host}{API_URL}EntityDefinitions(LogicalName='{entityLogicalName}')?$select=EntitySetName", ct);

        var setName = JsonDocument.Parse(json).RootElement
                                  .GetProperty("EntitySetName").GetString()
                                 ?? throw new InvalidOperationException(
                                      $"EntitySetName missing for {entityLogicalName}");

        _entitySetCache[entityLogicalName] = setName;
        return setName;
    }


    private static readonly HashSet<string> SupportedAttributeTypes = new()
    {
        "String",
        "Boolean",
        "DateTime",
        "Decimal",
        "Double",
        "Owner",
        "Integer",
        "Picklist",
        "Lookup",
        "Money"
    };

    private static readonly ConcurrentDictionary<string, string> _navCache = new();

    private static async Task<string?> GetNavPropAsync(
            HttpClient http, string host,
            string table, string attributeLogical,
            CancellationToken ct)
    {
        var cacheKey = $"{table}:{attributeLogical}";
        if (_navCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var url =
            $"https://{host}{API_URL}" +
            $"EntityDefinitions(LogicalName='{table}')?" +
            "$select=LogicalName&" +
            "$expand=ManyToOneRelationships(" +
            "$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)";

        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        foreach (var rel in doc.RootElement
                               .GetProperty("ManyToOneRelationships")
                               .EnumerateArray())
        {
            if (rel.GetProperty("ReferencingAttribute").GetString()?
                  .Equals(attributeLogical, StringComparison.OrdinalIgnoreCase) == true)
            {
                var nav = rel.GetProperty("ReferencingEntityNavigationPropertyName").GetString();
                if (!string.IsNullOrEmpty(nav))
                {
                    _navCache[cacheKey] = nav;
                    return nav;
                }
            }
        }
        return null;    // should never happen for a valid lookup
    }


    private static string BindColumn(AttributeMetadata meta)
    {
        // Prefer SchemaName because it always exists and always ends with 'Id'
        var name = meta.SchemaName ?? meta.LogicalName;

        // 👉 Ensure the column ends with "id"
        if (!name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            name += "Id";

        // Schemaname is PascalCase → down-case so the Web API likes it
        return name.ToLowerInvariant();            // fakton_projectdienstid
    }


    public static IEnumerable<AttributeMetadata> GetSupportedAttributes(
        this IEnumerable<AttributeMetadata> attributes)
        => attributes.Where(a => SupportedAttributeTypes.Contains(a.AttributeType));

    // 1. Map ELICIT answers → Dataverse payload
    public static async Task<Dictionary<string, object?>> MapElicitToPayload(
        this IDictionary<string, JsonElement> answers,
        IEnumerable<AttributeMetadata> attributes,
        HttpClient httpClient,
        string host,
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>();
        var attrMap = attributes.ToDictionary(a => a.LogicalName, a => a);

        foreach (var (key, json) in answers)
        {
            if (!attrMap.TryGetValue(key, out var meta)) continue;

            switch (meta.AttributeType)
            {
                case "Boolean":
                    payload[key] = json.ValueKind == JsonValueKind.String
                                   ? json.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase)
                                   : json.GetBoolean();
                    break;

                case "DateTime":
                    if (json.ValueKind == JsonValueKind.String
                      && !string.IsNullOrEmpty(json.ToString()))
                        payload[key] = json.GetDateTimeOffset();
                    else if (DateTime.TryParse(json.GetString(),
                                              CultureInfo.InvariantCulture, out var dec))
                        payload[key] = dec;
                    break;
                case "Decimal":
                case "Double":
                case "Money":
                    if (json.ValueKind == JsonValueKind.Number)
                        payload[key] = json.GetDecimal();
                    else if (decimal.TryParse(json.GetString(), NumberStyles.Any,
                                              CultureInfo.InvariantCulture, out var dec))
                        payload[key] = dec;
                    break;

                case "Integer":
                    if (json.ValueKind == JsonValueKind.Number)
                        payload[key] = json.GetInt32();
                    else if (int.TryParse(json.GetString(), out var i))
                        payload[key] = i;
                    break;

                case "Lookup":
                    {
                        var guid = json.GetString();
                        if (string.IsNullOrWhiteSpace(guid)) break;

                        // 1. Resolve the navigation property ------------------------------
                        var navProp = await GetNavPropAsync(
                                          httpClient, host,
                                          tableLogicalName,
                                          meta.LogicalName!, cancellationToken);
                        if (navProp is null) break;   // malformed metadata – bail out

                        // 2. Resolve target entity-set ------------------------------------
                        var target = meta.Targets?.FirstOrDefault()
                                  ?? (await GetLookupTargetsAsync(httpClient, host,
                                         tableLogicalName, meta.LogicalName!, cancellationToken))
                                     .FirstOrDefault();
                        if (string.IsNullOrEmpty(target)) break;

                        var entitySet = await GetEntitySetAsync(httpClient, host, target, cancellationToken);

                        // 3. Bind using the NAVIGATION property ---------------------------
                        payload[$"{navProp}@odata.bind"] = $"/{entitySet}({guid})";
                        break;
                    }


                case "Owner":
                    {
                        var guid = json.GetString();
                        if (string.IsNullOrWhiteSpace(guid))
                            break;

                        // Ask the user which type they supplied (systemuser or team).
                        // Your elicit schema already forces them to paste the GUID,
                        // so let’s *assume* it’s a user first and fall back to team.
                        //   foreach (var target in new[] { "systemuser", "team" })
                        foreach (var target in new[] { "systemuser" })
                        {
                            var entitySet = await GetEntitySetAsync(
                                                httpClient, host, target, cancellationToken);

                            // Try to bind. If the caller has no privilege to assign to teams,
                            // Dataverse will tell us with 400/403 and we can catch that in the outer
                            // POST; no need for an up-front HEAD probe.
                            payload["ownerid@odata.bind"] = $"/{entitySet}({guid})";
                            break;
                        }
                        break;
                    }

                default: // String, Memo, etc.
                    payload[key] = !string.IsNullOrEmpty(json.GetString()) ? json.GetString() : null;
                    break;
            }
        }

        return payload;
    }

    public static async Task<EntityMetadata> GetEntityMetadataAsync(
        this HttpClient http, string dynamicsHost, string tableLogicalName, CancellationToken ct)
    {
        var requestUrl =
            $"https://{dynamicsHost}{API_URL}" +
            $"EntityDefinitions(LogicalName='{tableLogicalName.ToLowerInvariant()}')" +
            "?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute&LabelLanguages=1033" +
            "&$expand=Attributes(" +
            "$select=LogicalName,SchemaName,DisplayName," +
            "AttributeType,RequiredLevel,IsValidForCreate,IsPrimaryId," +
            "IsLogical)";

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        req.Headers.Accept.ParseAdd(MediaTypeNames.Application.Json);

        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            throw new Exception(await res.Content.ReadAsStringAsync(ct));
        }

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<EntityMetadata>(json)!;
    }

    private static async Task<string[]> GetLookupTargetsAsync(
        HttpClient http, string host, string table, string lookupLogical,
        CancellationToken ct = default)
    {
        var url =
            $"https://{host}{API_URL}" +
            $"EntityDefinitions(LogicalName='{table}')/" +
            $"Attributes(LogicalName='{lookupLogical}')/" +
            "Microsoft.Dynamics.CRM.LookupAttributeMetadata?$select=Targets";

        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return [.. doc.RootElement.GetProperty("Targets")
                  .EnumerateArray()
                  .Select(t => t.GetString()!)];
    }


    /// <summary>
    /// Maps Dataverse <see cref="AttributeMetadata"/> to ELICIT‑compatible
    /// <see cref="ElicitRequestParams.PrimitiveSchemaDefinition"/> objects,
    /// covering every Dataverse primitive type you can create.
    /// </summary>
    public static async Task<Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>>
        MapMetadataToElicit(this IEnumerable<AttributeMetadata> attributes,
            string host,
            HttpClient http,
            string tableName,
            CancellationToken cancellationToken = default)
    {
        var props = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();

        foreach (var a in attributes.Where(x =>
                     x.IsValidForCreate && !x.IsPrimaryId && !x.IsLogical))
        {
            ElicitRequestParams.PrimitiveSchemaDefinition s;

            switch (a.AttributeType)
            {
                case "Picklist":
                    {
                        var opts = a.OptionSet?.Options ??
                                   a.GlobalOptionSet?.Options ??
                                   await GetPicklistOptionsAsync(http, host,
                                         host.Split('.')[0], a.LogicalName!, cancellationToken);

                        var enumSchema = new ElicitRequestParams.EnumSchema
                        {
                            Enum = [.. opts.Select(o => o.Value.ToString())],
                            EnumNames = [.. opts.Select(o => o.Label.UserLocalizedLabel.Label)]
                        };

                        s = enumSchema;
                        break;
                    }

                case "Lookup":
                case "Owner":
                    {
                        var lookupTargets = await GetLookupTargetsAsync(http, host, tableName, a.LogicalName!, cancellationToken);
                        if (lookupTargets.Length == 1)
                        {
                            var (_, _, ids, names) = await GetLookupChoicesAsync(http, host, lookupTargets[0], cancellationToken);
                            s = new ElicitRequestParams.EnumSchema { Enum = ids, EnumNames = names };
                        }
                        else
                        {
                            s = new ElicitRequestParams.StringSchema { Description = "Paste GUID of owner (systemuser/team)" };
                        }
                        break;
                    }
                case "Boolean": s = new ElicitRequestParams.BooleanSchema(); break;
                case "DateTime": s = new ElicitRequestParams.StringSchema { Format = "date-time" }; break;
                case "Decimal" or "Double" or "Money" or "Integer":
                    s = new ElicitRequestParams.NumberSchema(); break;
                default: s = new ElicitRequestParams.StringSchema(); break;
            }

            s.Title = a.LogicalName ?? a.SchemaName;
            //   s.Title = a.SchemaName ?? a.SchemaName;

            props[a.LogicalName!] = s;
        }

        return props;
    }

    private static async Task<Option[]> GetPicklistOptionsAsync(
        this HttpClient http, string host, string entity, string attr, CancellationToken ct)
    {
        var uri = $"https://{host}{API_URL}" +
                  $"EntityDefinitions(LogicalName='{entity}')/" +
                  $"Attributes(LogicalName='{attr}')/" +
                  "Microsoft.Dynamics.CRM.PicklistAttributeMetadata?" +
                  "$select=LogicalName&" +
                  "$expand=OptionSet($select=Options),GlobalOptionSet($select=Options)";
        var json = await http.GetStringAsync(uri, ct);
        using var doc = JsonDocument.Parse(json);
        var optsNode = doc.RootElement
                          .GetProperty("OptionSet")
                          .GetProperty("Options");
        return optsNode.Deserialize<Option[]>() ?? [];
    }

    private static async Task<(string idField, string nameField, string[] ids, string[] names)>
        GetLookupChoicesAsync(this HttpClient http, string host, string targetLogical, CancellationToken ct)
    {
        var meta = await http.GetStringAsync(
            $"https://{host}{API_URL}EntityDefinitions(LogicalName='{targetLogical}')?" +
            "$select=EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute", ct);

        using var m = JsonDocument.Parse(meta);
        var entSet = m.RootElement.GetProperty("EntitySetName").GetString();
        var idAttr = m.RootElement.GetProperty("PrimaryIdAttribute").GetString();
        var nameAttr = m.RootElement.GetProperty("PrimaryNameAttribute").GetString();

        var rows = await http.GetStringAsync(
            $"https://{host}{API_URL}{entSet}?$select={idAttr},{nameAttr}&$top=5000", ct);

        using var r = JsonDocument.Parse(rows);
        var items = r.RootElement.GetProperty("value").EnumerateArray()
            .Select(e => (
                id: e.GetProperty(idAttr!).GetString()!,
                name: e.TryGetProperty(nameAttr!, out var n) ? n.GetString() ?? "(no name)" : "(no name)")
            )
            .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ids = items.Select(x => x.id).ToArray();
        var names = items.Select(x => x.name).ToArray();

        return (idAttr!, nameAttr!, ids, names);
    }
}

