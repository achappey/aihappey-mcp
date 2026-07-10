using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Lists;

public static class GraphListItems
{

    [Description("Update a Microsoft List item")]
    [McpServerTool(Title = "Update a Microsoft List item", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_UpdateListItem(
      string siteId,            // ID of the SharePoint site
      string listId,            // ID of the Microsoft List
      string itemId,            // ID of the Microsoft List item
      RequestContext<CallToolRequestParams> requestContext,
      [Description("Default values for the list item fields. Use fieldname as key and defaultvalue as value. No nested objects. These override the current item values in the form.")]
      Dictionary<string, object?>? defaultValues = null,
      CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
{
    var list = await client
          .Sites[siteId]
          .Lists[listId]
          .GetAsync(cancellationToken: cancellationToken);

    var item = await client
          .Sites[siteId]
          .Lists[listId]
          .Items[itemId]
          .GetAsync(requestConfiguration =>
          {
              requestConfiguration.QueryParameters.Expand = ["fields"];
          }, cancellationToken: cancellationToken);

    var columns = await client
           .Sites[siteId]
           .Lists[listId]
           .Columns
           .GetAsync(cancellationToken: cancellationToken);

    ElicitRequestParams.RequestSchema request = new()
    {
        Required = []
    };

    var defaultValuesByName = new Dictionary<string, object?>();

    foreach (var kv in item?.Fields?.AdditionalData ?? new Dictionary<string, object>())
    {
        defaultValuesByName[kv.Key] = kv.Value;
    }

    foreach (var kv in defaultValues ?? [])
    {
        defaultValuesByName[kv.Key] = kv.Value;
    }

    var definitionColumns = columns?.Value?
        .Where(col => col.Name != "ID" && col.ReadOnly != true && !string.IsNullOrWhiteSpace(col.Name))
        .Select(col =>
        {
            defaultValuesByName.TryGetValue(col.Name!, out var defaultValue);

            return new
            {
                Name = col.Name!,
                Def = col.ToElicitSchemaDef(defaultValue),
                col.Required
            };
        })
        .Where(x => x.Def != null)
        .ToList();

    foreach (var col in definitionColumns ?? [])
    {
        request.Properties.Add(col.Name, col.Def!);

        if (col.Required == true)
        {
            request.Required.Add(col.Name);
        }
    }

    var elicitResult = await requestContext.Server.ElicitAsync(new ElicitRequestParams()
    {
        RequestedSchema = request,
        Message = list?.DisplayName ?? list?.Name ?? "Update SharePoint list item"
    }, cancellationToken: cancellationToken);

    var values = elicitResult?.Content;

    if (values is null)
        throw new Exception("No values returned.");

    var defsByName = (columns?.Value ?? [])
        .Where(c => !string.IsNullOrWhiteSpace(c.Name))
        .ToDictionary(c => c.Name!, c => c);

    var fieldsPayload = new Dictionary<string, object>();

    foreach (var prop in request.Properties.Keys)
    {
        if (!values.TryGetValue(prop, out var raw)) continue;

        object? val = raw;

        // ---- unwrap JsonElement safely
        if (raw is JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    continue;
                case JsonValueKind.String:
                    var s = je.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    val = s;
                    break;
                case JsonValueKind.Array:
                    var seq = je.EnumerateArray()
                                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .ToList();
                    if (seq.Count == 0) continue;
                    val = seq;
                    break;
                case JsonValueKind.Number:
                    if (je.TryGetInt64(out var i)) val = i;
                    else if (je.TryGetDouble(out var d)) val = d;
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    val = je.GetBoolean();
                    break;
            }
        }

        if (!defsByName.TryGetValue(prop, out var def))
        {
            if (val is string s2 && string.IsNullOrWhiteSpace(s2)) continue;
            fieldsPayload[prop] = val!;
            continue;
        }

        // ---- Date/DateTime
        if (def.DateTime != null)
        {
            var fmtStr = def.DateTime.Format?.ToString();
            var isDateOnly = string.Equals(fmtStr, "dateOnly", StringComparison.OrdinalIgnoreCase);

            string? isoOut = null;

            if (val is string ds)
            {
                if (isDateOnly)
                {
                    if (DateOnly.TryParse(ds, out var d))
                        isoOut = d.ToString("yyyy-MM-dd");
                    else if (DateTimeOffset.TryParse(ds, out var dto))
                        isoOut = dto.Date.ToString("yyyy-MM-dd");
                }
                else
                {
                    if (DateTimeOffset.TryParse(ds, out var dto))
                        isoOut = dto.ToUniversalTime().ToString("o");
                    else if (DateTime.TryParse(ds, out var dt))
                        isoOut = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");
                }
            }
            else if (val is DateTime dt)
            {
                isoOut = isDateOnly
                    ? dt.Date.ToString("yyyy-MM-dd")
                    : DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");
            }
            else if (val is DateTimeOffset dto)
            {
                isoOut = isDateOnly
                    ? dto.Date.ToString("yyyy-MM-dd")
                    : dto.ToUniversalTime().ToString("o");
            }

            if (!string.IsNullOrWhiteSpace(isoOut))
                fieldsPayload[prop] = isoOut;

            continue;
        }

        // ---- Choice (single / multi)
        if (def.Choice != null || (def.AdditionalData?.ContainsKey("multiChoice") == true))
        {
            var isMulti = def.AdditionalData?.ContainsKey("multiChoice") == true;

            if (isMulti)
            {
                if (val is IEnumerable<object> many)
                {
                    var arr = many.Select(x => x?.ToString())
                                  .Where(x => !string.IsNullOrWhiteSpace(x))
                                  .ToArray();
                    if (arr.Length > 0) fieldsPayload[prop] = arr;
                }
                else if (val is string one && !string.IsNullOrWhiteSpace(one))
                {
                    fieldsPayload[prop] = new[] { one };
                }
            }
            else
            {
                if (val is string one && !string.IsNullOrWhiteSpace(one))
                    fieldsPayload[prop] = one;
            }

            continue;
        }

        // ---- Default: keep non-empty
        if (val is string s3 && string.IsNullOrWhiteSpace(s3)) continue;
        fieldsPayload[prop] = val!;
    }

    if (fieldsPayload.Count == 0)
        throw new Exception("No field values supplied.");

    return await client
        .Sites[siteId]
        .Lists[listId]
        .Items[itemId]
        .Fields
        .PatchAsync(new Microsoft.Graph.Beta.Models.FieldValueSet
        {
            AdditionalData = fieldsPayload
        }, cancellationToken: cancellationToken);
})));

    [Description("Create a new Microsoft List item")]
    [McpServerTool(Title = "Create a new Microsoft List item", Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_CreateListItem(
          string siteId,            // ID of the SharePoint site
          string listId,            // ID of the Microsoft List
          RequestContext<CallToolRequestParams> requestContext,
           [Description("Default values for the new list item fields. Use fieldname as key and defaultvalue as value. No nested objects.")]
            Dictionary<string, object?>? defaultValues = null,
          CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
    {
        //  if (defaultValues == null || !defaultValues.Any())
        //    {
        //      throw new Exception("Default values missing.");
        //   }

        var list = await client
              .Sites[siteId]
              .Lists[listId].GetAsync(cancellationToken: cancellationToken);

        var columns = await client
               .Sites[siteId]
               .Lists[listId]
               .Columns
               .GetAsync(cancellationToken: cancellationToken);

        ElicitRequestParams.RequestSchema request = new()
        {
            Required = []
        };

        /*  var definitionColumns = columns?.Value?.Where(col => col.Name != "ID" && col.ReadOnly != true)
              .ToDictionary(a => a.Name!, a => new
              {
                  def = a.ToElicitSchemaDef(),
                  req = a.Required
              })
              .Where(a => a.Value.def != null);*/

        var defaultValuesByName = defaultValues ?? new Dictionary<string, object?>();

        var definitionColumns = columns?.Value?
            .Where(col => col.Name != "ID" && col.ReadOnly != true && !string.IsNullOrWhiteSpace(col.Name))
            .Select(col =>
            {
                defaultValuesByName.TryGetValue(col.Name!, out var defaultValue);

                return new
                {
                    Name = col.Name!,
                    Def = col.ToElicitSchemaDef(defaultValue),
                    col.Required
                };
            })
            .Where(x => x.Def != null)
            .ToList();

        foreach (var col in definitionColumns ?? [])
        {
            request.Properties.Add(col.Name, col.Def!);

            if (col.Required == true)
            {
                request.Required.Add(col.Name);
            }
        }

        /*  foreach (var col in definitionColumns ?? [])
          {
              request.Properties.Add(col.Key, col.Value.def!);

              if (col.Value.req == true)
              {
                  request.Required.Add(col.Key);
              }
          }*/

        var elicitResult = await requestContext.Server.ElicitAsync(new ElicitRequestParams()
        {
            RequestedSchema = request,
            Message = list?.DisplayName ?? list?.Name ?? "New SharePoint list item"
        }, cancellationToken: cancellationToken);

        var values = elicitResult?.Content;

        var defsByName = (columns?.Value ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToDictionary(c => c.Name!, c => c);

        var fieldsPayload = new Dictionary<string, object>();

        foreach (var prop in request.Properties.Keys)
        {
            if (!values!.TryGetValue(prop, out var raw)) continue;

            object? val = raw;

            // ---- unwrap JsonElement safely
            if (raw is JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        continue;
                    case JsonValueKind.String:
                        var s = je.GetString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        val = s;
                        break;
                    case JsonValueKind.Array:
                        var seq = je.EnumerateArray()
                                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .ToList();
                        if (seq.Count == 0) continue;
                        val = seq;
                        break;
                    case JsonValueKind.Number:
                        if (je.TryGetInt64(out var i)) val = i;
                        else if (je.TryGetDouble(out var d)) val = d;
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        val = je.GetBoolean();
                        break;
                }
            }

            if (!defsByName.TryGetValue(prop, out var def))
            {
                if (val is string s2 && string.IsNullOrWhiteSpace(s2)) continue;
                fieldsPayload[prop] = val!;
                continue;
            }

            // ---- Date/DateTime
            if (def.DateTime != null)
            {
                var fmtStr = def.DateTime.Format?.ToString(); // SDK may expose enum or string
                var isDateOnly = string.Equals(fmtStr, "dateOnly", StringComparison.OrdinalIgnoreCase);

                string? isoOut = null;

                if (val is string ds)
                {
                    if (isDateOnly)
                    {
                        if (DateOnly.TryParse(ds, out var d))
                            isoOut = d.ToString("yyyy-MM-dd");
                        else if (DateTimeOffset.TryParse(ds, out var dto))
                            isoOut = dto.Date.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        if (DateTimeOffset.TryParse(ds, out var dto))
                            isoOut = dto.ToUniversalTime().ToString("o");
                        else if (DateTime.TryParse(ds, out var dt))
                            isoOut = DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");
                    }
                }
                else if (val is DateTime dt)
                {
                    isoOut = isDateOnly
                        ? dt.Date.ToString("yyyy-MM-dd")
                        : DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");
                }
                else if (val is DateTimeOffset dto)
                {
                    isoOut = isDateOnly
                        ? dto.Date.ToString("yyyy-MM-dd")
                        : dto.ToUniversalTime().ToString("o");
                }

                if (!string.IsNullOrWhiteSpace(isoOut))
                    fieldsPayload[prop] = isoOut;

                continue;
            }

            // ---- Choice (single / multi)
            if (def.Choice != null || (def.AdditionalData?.ContainsKey("multiChoice") == true))
            {
                var isMulti = def.AdditionalData?.ContainsKey("multiChoice") == true;

                if (isMulti)
                {
                    if (val is IEnumerable<object> many)
                    {
                        var arr = many.Select(x => x?.ToString())
                                      .Where(x => !string.IsNullOrWhiteSpace(x))
                                      .ToArray();
                        if (arr.Length > 0) fieldsPayload[prop] = arr;
                    }
                    else if (val is string one && !string.IsNullOrWhiteSpace(one))
                    {
                        fieldsPayload[prop] = new[] { one };
                    }
                }
                else
                {
                    if (val is string one && !string.IsNullOrWhiteSpace(one))
                        fieldsPayload[prop] = one;
                }

                continue;
            }

            // ---- Default: keep non-empty
            if (val is string s3 && string.IsNullOrWhiteSpace(s3)) continue;
            fieldsPayload[prop] = val!;
        }

        return await client
            .Sites[siteId]
            .Lists[listId]
            .Items
            .PostAsync(new Microsoft.Graph.Beta.Models.ListItem
            {
                Fields = new Microsoft.Graph.Beta.Models.FieldValueSet
                {
                    AdditionalData = fieldsPayload
                }
            }, cancellationToken: cancellationToken);
    })));
 
    [Description("Delete a Microsoft List item")]
    [McpServerTool(Title = "Delete a Microsoft List item", Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_DeleteListItem(
        [Description("ID of the SharePoint site (e.g. 'contoso.sharepoint.com,GUID,GUID')")]
            string siteId,
        [Description("ID of the Microsoft List")]
            string listId,
        [Description("ID of the list item to delete")]
            string itemId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<DeleteSharePointListItem>(
            itemId,
            async _ =>
            {
                // Perform actual deletion
                await client
                    .Sites[siteId]
                    .Lists[listId]
                    .Items[itemId]
                    .DeleteAsync(cancellationToken: cancellationToken);
            },
            "List item deleted successfully.",
            cancellationToken
        )));

    /// <summary>
    /// Used to confirm deletion of a SharePoint List item.
    /// </summary>
    [Description("Please fill in the list item ID to confirm deletion: {0}")]
    public class DeleteSharePointListItem : IHasName
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("The ID of the list item to delete.")]
        public string Name { get; set; } = default!;
    }  


}
