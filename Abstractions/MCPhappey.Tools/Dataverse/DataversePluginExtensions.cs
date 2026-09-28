using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Mime;
using System.Text.RegularExpressions;

namespace MCPhappey.Tools.Dataverse;

public static class DataversePluginExtensions
{

    public static readonly string API_URL = "/api/data/v9.2/";

    private static readonly ConcurrentDictionary<string, string> _entitySetCache = new();

    private static async Task<string> GetEntitySetAsync(
            HttpClient httpClient, string host,
            string entityLogicalName, CancellationToken ct)
    {
        var cacheKey = $"{host}:{entityLogicalName}";
        if (_entitySetCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var json = await httpClient.GetStringAsync(
            $"https://{host}{API_URL}EntityDefinitions(LogicalName='{entityLogicalName}')?$select=EntitySetName", ct);

        var setName = JsonDocument.Parse(json).RootElement
                                  .GetProperty("EntitySetName").GetString()
                                 ?? throw new InvalidOperationException(
                                      $"EntitySetName missing for {entityLogicalName}");

        _entitySetCache[cacheKey] = setName;
        return setName;
    }


    private static readonly HashSet<string> SupportedAttributeTypes =
    [
        "String",
        "Boolean",
        "DateTime",
        "Decimal",
        "Double",
        "Owner",
        "Integer",
        "Picklist",
        "State",
        "Status",
        "Lookup",
        "Money"
    ];

    private static readonly ConcurrentDictionary<string, string> _navCache = new();

    private static async Task<Dictionary<string, (string Attribute, string Target)>> GetLookupRelationshipsAsync(
        HttpClient http, string host, string table, CancellationToken ct)
    {
        var url = $"https://{host}{API_URL}EntityDefinitions(LogicalName='{table}')?" +
                  "$select=LogicalName&$expand=ManyToOneRelationships(" +
                  "$select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName)";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        return doc.RootElement.GetProperty("ManyToOneRelationships").EnumerateArray()
            .Where(r => r.TryGetProperty("ReferencingEntityNavigationPropertyName", out var nav) &&
                        nav.ValueKind == JsonValueKind.String &&
                        r.TryGetProperty("ReferencingAttribute", out var attr) && attr.ValueKind == JsonValueKind.String &&
                        r.TryGetProperty("ReferencedEntity", out var target) && target.ValueKind == JsonValueKind.String)
            .ToDictionary(r => r.GetProperty("ReferencingEntityNavigationPropertyName").GetString()!,
                r => (r.GetProperty("ReferencingAttribute").GetString()!, r.GetProperty("ReferencedEntity").GetString()!),
                StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<(Dictionary<string, object?> Values, Dictionary<string, string> LookupTargets)> NormalizeReplacementsAsync(
        this IReadOnlyDictionary<string, object?> replacements, IEnumerable<AttributeMetadata> attributes,
        HttpClient http, string host, string table, CancellationToken ct)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowed = attributes.ToDictionary(a => a.LogicalName, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (string Attribute, string Target)>? relationships = null;

        foreach (var (key, value) in replacements)
        {
            if (key.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase))
            {
                relationships ??= await GetLookupRelationshipsAsync(http, host, table, ct);
                var nav = key[..^"@odata.bind".Length];
                if (!relationships.TryGetValue(nav, out var relation) ||
                    !allowed.TryGetValue(relation.Attribute, out var attribute) ||
                    attribute.AttributeType is not ("Lookup" or "Owner"))
                    throw new ArgumentException($"Unknown or unwritable lookup binding '{key}'.");

                var match = Regex.Match(value?.ToString() ?? "", @"^/?(?<set>[\w]+)\((?<id>[0-9a-fA-F-]{36})\)$");
                if (!match.Success || !Guid.TryParse(match.Groups["id"].Value, out var id) ||
                    !string.Equals(match.Groups["set"].Value,
                        await GetEntitySetAsync(http, host, relation.Target, ct), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Invalid Dataverse lookup binding '{key}'.");

                if (values.ContainsKey(attribute.LogicalName))
                    throw new ArgumentException($"Conflicting values for lookup '{attribute.LogicalName}'.");
                values.Add(attribute.LogicalName, id.ToString());
                targets.Add(attribute.LogicalName, relation.Target);
            }
            else
            {
                if (!allowed.ContainsKey(key))
                    throw new ArgumentException($"Unknown or unwritable Dataverse attribute '{key}'.");
                if (values.ContainsKey(key))
                    throw new ArgumentException($"Conflicting values for attribute '{key}'.");
                values.Add(key, value);
            }
        }

        return (values, targets);
    }

    private static async Task<string?> GetNavPropAsync(
            HttpClient http, string host,
            string table, string attributeLogical,
            CancellationToken ct)
    {
        var cacheKey = $"{host}:{table}:{attributeLogical}";
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
        this IEnumerable<AttributeMetadata> attributes, bool forCreate = true)
        => attributes.Where(a => SupportedAttributeTypes.Contains(a.AttributeType)
            && (forCreate ? a.IsValidForCreate : a.IsValidForUpdate)
            && !a.IsPrimaryId && !a.IsLogical);

    // 1. Map ELICIT answers → Dataverse payload
    public static async Task<Dictionary<string, object?>> MapElicitToPayload(
        this IDictionary<string, JsonElement> answers,
        IEnumerable<AttributeMetadata> attributes,
        HttpClient httpClient,
        string host,
        string tableLogicalName,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? lookupTargets = null)
    {
        var payload = new Dictionary<string, object?>();
        var attrMap = attributes.ToDictionary(a => a.LogicalName, a => a, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, json) in answers)
        {
            if (!attrMap.TryGetValue(key, out var meta))
                throw new ArgumentException($"Unknown Dataverse attribute '{key}'.");

            if (!SupportedAttributeTypes.Contains(meta.AttributeType) || meta.IsPrimaryId || meta.IsLogical)
                throw new ArgumentException($"Dataverse attribute '{key}' is not supported for writing.");

            if (json.ValueKind == JsonValueKind.Null)
            {
                if (meta.AttributeType is "Lookup" or "Owner")
                {
                    var nav = await GetNavPropAsync(httpClient, host, tableLogicalName, meta.LogicalName, cancellationToken)
                        ?? throw new InvalidOperationException($"No navigation property found for '{tableLogicalName}.{key}'.");
                    payload[$"{nav}@odata.bind"] = null;
                }
                else
                    payload[meta.LogicalName] = null;
                continue;
            }

            // Blank form fields mean "not supplied". Clearing an existing value requires explicit JSON null.
            if (json.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(json.GetString()))
                continue;

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
                case "Picklist":
                case "State":
                case "Status":
                    if (json.ValueKind == JsonValueKind.Number)
                        payload[key] = json.GetInt32();
                    else if (json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        payload[key] = i;
                    else
                        throw new ArgumentException($"Attribute '{key}' requires an integer value.");
                    break;

                case "Lookup":
                    {
                        var guid = json.GetString();
                        if (string.IsNullOrWhiteSpace(guid)) break;
                        if (!Guid.TryParse(guid, out var parsedGuid))
                            throw new ArgumentException($"Attribute '{key}' requires a GUID.");

                        // 1. Resolve the navigation property ------------------------------
                        var navProp = await GetNavPropAsync(
                                          httpClient, host,
                                          tableLogicalName,
                                          meta.LogicalName!, cancellationToken);
                        if (navProp is null)
                            throw new InvalidOperationException($"No navigation property found for '{tableLogicalName}.{key}'.");

                        // 2. Resolve target entity-set ------------------------------------
                        var target = (lookupTargets?.TryGetValue(meta.LogicalName, out var selectedTarget) == true ? selectedTarget : null)
                                   ?? meta.Targets?.FirstOrDefault()
                                  ?? (await GetLookupTargetsAsync(httpClient, host,
                                         tableLogicalName, meta.LogicalName!, cancellationToken))
                                     .FirstOrDefault();
                        if (string.IsNullOrEmpty(target))
                            throw new InvalidOperationException($"No lookup target found for '{tableLogicalName}.{key}'.");

                        var entitySet = await GetEntitySetAsync(httpClient, host, target, cancellationToken);

                        // 3. Bind using the NAVIGATION property ---------------------------
                        payload[$"{navProp}@odata.bind"] = $"/{entitySet}({parsedGuid})";
                        break;
                    }


                case "Owner":
                    {
                        var guid = json.GetString();
                        if (string.IsNullOrWhiteSpace(guid))
                            break;
                        if (!Guid.TryParse(guid, out var parsedGuid))
                            throw new ArgumentException($"Attribute '{key}' requires a GUID.");

                        var target = lookupTargets?.TryGetValue(meta.LogicalName, out var selectedTarget) == true
                            ? selectedTarget : "systemuser";
                        var entitySet = await GetEntitySetAsync(httpClient, host, target, cancellationToken);
                        var nav = await GetNavPropAsync(httpClient, host, tableLogicalName, meta.LogicalName, cancellationToken)
                            ?? throw new InvalidOperationException($"No navigation property found for '{tableLogicalName}.{key}'.");
                        payload[$"{nav}@odata.bind"] = $"/{entitySet}({parsedGuid})";
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
            "AttributeType,RequiredLevel,IsValidForCreate,IsValidForUpdate,IsPrimaryId," +
            "IsLogical)";

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        req.Headers.Accept.ParseAdd(MediaTypeNames.Application.Json);

        using var res = await http.SendAsync(req, ct);

        if (!res.IsSuccessStatusCode)
        {
            throw new Exception(await res.Content.ReadAsStringAsync(ct));
        }

        var json = await res.Content.ReadAsStringAsync(ct);
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
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, object?>? defaultValues = null)
    {
        var props = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>();

        foreach (var a in attributes)
        {
            ElicitRequestParams.PrimitiveSchemaDefinition s;
            object? defaultValue = null;
            defaultValues?.TryGetValue(a.LogicalName, out defaultValue);
            var textDefault = defaultValue?.ToString();
            if (string.IsNullOrWhiteSpace(textDefault)) textDefault = null;

            switch (a.AttributeType)
            {
                case "Picklist" or "State" or "Status":
                    {
                        var opts = a.OptionSet?.Options ??
                                   a.GlobalOptionSet?.Options ??
                                   await GetPicklistOptionsAsync(http, host,
                                          tableName, a.LogicalName!, a.AttributeType, cancellationToken);

                        var enumSchema = new ElicitRequestParams.TitledSingleSelectEnumSchema
                        {
                            Default = textDefault,
                            OneOf = [.. opts.Select(o => new ElicitRequestParams.EnumSchemaOption()
                            {
                                 Title =  o.Label?.UserLocalizedLabel?.Label
                                    ?? o.Value.ToString(),
                                Const =  o.Value.ToString()
                            })]
                        };

                        s = enumSchema;
                        break;
                    }

                case "Lookup":
                    {
                        var lookupTargets = await GetLookupTargetsAsync(http, host, tableName, a.LogicalName!, cancellationToken);
                        if (lookupTargets.Length == 1)
                        {
                            var (_, _, ids, names) = await GetLookupChoicesAsync(http, host, lookupTargets[0], cancellationToken);
                            s = new ElicitRequestParams.TitledSingleSelectEnumSchema
                            {
                                Default = textDefault,
                                OneOf = [.. ids.Select((o, i) => new ElicitRequestParams.EnumSchemaOption()
                            {
                                Title =  names[i],
                                Const = o
                            })],
                            };
                        }
                        else
                        {
                            s = new ElicitRequestParams.StringSchema { Description = "Paste GUID of referenced record", Default = textDefault };
                        }
                        break;
                    }
                case "Owner": s = new ElicitRequestParams.StringSchema { Description = "Paste GUID of owning systemuser", Default = textDefault }; break;
                case "Boolean": s = new ElicitRequestParams.BooleanSchema { Default = bool.TryParse(textDefault, out var boolean) ? boolean : null }; break;
                case "DateTime": s = new ElicitRequestParams.StringSchema { Format = "date-time", Default = textDefault }; break;
                case "Decimal" or "Double" or "Money" or "Integer":
                    s = new ElicitRequestParams.NumberSchema { Default = double.TryParse(textDefault, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null }; break;
                default: s = new ElicitRequestParams.StringSchema { Default = textDefault }; break;
            }

            s.Title = a.LogicalName ?? a.SchemaName;
            //   s.Title = a.SchemaName ?? a.SchemaName;

            props[a.LogicalName!] = s;
        }

        return props;
    }

    private static async Task<Option[]> GetPicklistOptionsAsync(
        this HttpClient http, string host, string entity, string attr, string attributeType, CancellationToken ct)
    {
        var metadataType = attributeType switch
        {
            "State" => "StateAttributeMetadata",
            "Status" => "StatusAttributeMetadata",
            _ => "PicklistAttributeMetadata"
        };
        var uri = $"https://{host}{API_URL}" +
                   $"EntityDefinitions(LogicalName='{entity}')/" +
                   $"Attributes(LogicalName='{attr}')/" +
                   $"Microsoft.Dynamics.CRM.{metadataType}?" +
                   "$select=LogicalName&$expand=OptionSet,GlobalOptionSet";
        using var response = await http.GetAsync(uri, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Dataverse option metadata for '{entity}.{attr}' failed ({(int)response.StatusCode}): {json}", null, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        foreach (var property in new[] { "OptionSet", "GlobalOptionSet" })
        {
            if (doc.RootElement.TryGetProperty(property, out var set) && set.ValueKind == JsonValueKind.Object &&
                set.TryGetProperty("Options", out var options) && options.ValueKind == JsonValueKind.Array)
                return options.Deserialize<Option[]>() ?? [];
        }

        throw new InvalidOperationException($"Dataverse option metadata for '{entity}.{attr}' contains no option set.");
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

