using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Lists;

public static class GraphLists
{
    [Description("Create a new Microsoft List item")]
    [McpServerTool(Title = "Create a new Microsoft List item", Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult> GraphLists_CreateListItem(
          string siteId,            // ID of the SharePoint site
          string listId,            // ID of the Microsoft List
    //      [Description("More background info around the list item creation")] string comments,
          RequestContext<CallToolRequestParams> requestContext,
          //     Dictionary<string, object> defaultValues,
          CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
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

        var definitionColumns = columns?.Value?.Where(col => col.Name != "ID" && col.ReadOnly != true)
            .ToDictionary(a => a.Name!, a => new
            {
                def = a.ToElicitSchemaDef(),
                req = a.Required
            })
            .Where(a => a.Value.def != null);

        foreach (var col in definitionColumns ?? [])
        {
            request.Properties.Add(col.Key, col.Value.def!);

            if (col.Value.req == true)
            {
                request.Required.Add(col.Key);
            }
        }

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

    [Description("Create a new Microsoft List")]
    [McpServerTool(Title = "Create a new Microsoft List", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_CreateList(
            [Description("ID of the SharePoint site (e.g. 'contoso.sharepoint.com,GUID,GUID')")]
        string siteId,
            [Description("Title of the new list")]
        string listTitle,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Title of the new list")]
        SharePointListTemplate template = SharePointListTemplate.genericList,
            [Description("Description of the new list")]
        string? description = null,
            CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent<Microsoft.Graph.Beta.Models.List?>(async () =>
        {
            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                new GraphNewSharePointList
                {
                    Title = listTitle,
                    Description = description,
                    Template = template
                },
                cancellationToken
            );

            return await client.Sites[siteId].Lists.PostAsync(
                new Microsoft.Graph.Beta.Models.List
                {
                    DisplayName = typed.Title,
                    Description = typed.Description,
                    ListProp = new Microsoft.Graph.Beta.Models.ListInfo
                    {
                        Template = typed.Template.ToString()
                    }
                },
                cancellationToken: cancellationToken
            );
        })));

    [Description("Add a column to a Microsoft List")]
    [McpServerTool(Title = "Add a column to a Microsoft List", Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_AddColumn(
            [Description("ID of the SharePoint site (e.g. 'contoso.sharepoint.com,GUID,GUID')")]
        string siteId,
            [Description("ID of the Microsoft List")]
        string listId,
            [Description("Column name")]
        string columnName,
            [Description("Column display name")]
        string? columnDisplayName,
            RequestContext<CallToolRequestParams> requestContext,
            [Description("Column type (e.g. text, number, boolean, dateTime, choice)")]
        SharePointColumnType columnType = SharePointColumnType.Text,
            [Description("Choices values. Comma seperated list.")]
        string? choices = null,
            CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async client =>
            await requestContext.WithStructuredContent(async () =>
        {
            var site = await client
                        .Sites[siteId]
                        .GetAsync(cancellationToken: cancellationToken);

            var list = await client
                .Sites[siteId]
                .Lists[listId]
                .GetAsync(cancellationToken: cancellationToken);

            var (typed, notAccepted, result) = await requestContext.Server.TryElicit(
                    new GraphNewSharePointColumn
                    {
                        DisplayName = columnDisplayName,
                        Name = columnName,
                        ColumnType = columnType,
                        Choices = choices
                    },
                    cancellationToken
                );

            // Build column based on type (jouw bestaande logic)
            var columnDef = GetColumnDefinition(typed.Name, typed.DisplayName ?? typed.Name, typed.ColumnType, typed.Choices);

            return await client.Sites[siteId].Lists[listId].Columns.PostAsync(
                columnDef,
                cancellationToken: cancellationToken
            );
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
        await requestContext.WithExceptionCheck(async () =>
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

    [Description("Please fill in the details for the new Microsoft List.")]
    public class GraphNewSharePointList
    {
        [JsonPropertyName("title")]
        [Required]
        [Description("Name of the new list")]
        public string Title { get; set; } = default!;

        [JsonPropertyName("description")]
        [Description("Description of the list (optional)")]
        public string? Description { get; set; }

        [JsonPropertyName("template")]
        [Description("Template type for the new list")]
        [Required]
        public SharePointListTemplate Template { get; set; } = SharePointListTemplate.genericList;
    }

    [Description("Please fill in the details for the new column.")]
    public class GraphNewSharePointColumn
    {
        [JsonPropertyName("name")]
        [Required]
        [Description("Column name (no spaces, unique in list)")]
        public string Name { get; set; } = default!;

        [JsonPropertyName("displayName")]
        [Description("Column display name (optional, for UI)")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("columnType")]
        [Required]
        [Description("Type of column")]
        public SharePointColumnType ColumnType { get; set; } = SharePointColumnType.Text;

        [JsonPropertyName("choices")]
        [Description("Choices (only for 'Choice' type), comma separated")]
        public string? Choices { get; set; }
    }

    private static Microsoft.Graph.Beta.Models.ColumnDefinition GetColumnDefinition(string name, string displayName, SharePointColumnType columnType, string? choices = null)
    {
        var col = new Microsoft.Graph.Beta.Models.ColumnDefinition
        {
            Name = name,
            DisplayName = displayName ?? name
        };

        switch (columnType)
        {
            case SharePointColumnType.Text:
                col.Text = new Microsoft.Graph.Beta.Models.TextColumn();
                break;
            case SharePointColumnType.Number:
                col.Number = new Microsoft.Graph.Beta.Models.NumberColumn();
                break;
            case SharePointColumnType.YesNo:
                col.Boolean = new Microsoft.Graph.Beta.Models.BooleanColumn();
                break;
            case SharePointColumnType.Choice:
                col.Choice = new Microsoft.Graph.Beta.Models.ChoiceColumn
                {
                    Choices = choices?.Split(',').Select(x => x.Trim()).ToList() ?? new List<string>()
                };
                break;
            case SharePointColumnType.DateTime:
                col.DateTime = new Microsoft.Graph.Beta.Models.DateTimeColumn();
                break;
            // Add more types as needed
            default:
                throw new NotImplementedException("Unsupported column type");
        }

        return col;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SharePointColumnType
    {
        [Description("Text (single line)")]
        Text,
        [Description("Number")]
        Number,
        [Description("Yes/No (boolean)")]
        YesNo,
        [Description("Choice (dropdown)")]
        Choice,
        [Description("Date/Time")]
        DateTime
        // Add more as needed
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SharePointListTemplate
    {
        [Description("Custom list (genericList)")]
        genericList,

        [Description("Document library (documentLibrary)")]
        [JsonPropertyName("documentLibrary")]
        documentLibrary,

        [Description("Task list (tasks)")]
        [JsonPropertyName("tasks")]
        tasks,

        [Description("Issue tracking (issues)")]
        [JsonPropertyName("issues")]
        issues,

        [Description("Calendar (events)")]
        [JsonPropertyName("events")]
        events
    }

}
