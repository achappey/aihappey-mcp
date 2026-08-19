using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Lists;

public static class GraphLists
{
    [Description("Create a new Microsoft List")]
    [McpServerTool(Title = "Create a new Microsoft List",
        Destructive = false,
        OpenWorld = false)]
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
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
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
    [McpServerTool(Title = "Add a column to a Microsoft List",
        Destructive = false,
        OpenWorld = false)]
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
            [Description("Choice displayAs value: dropDownMenu, radioButtons or checkBoxes")]
        string? choiceSelect = null,
            CancellationToken cancellationToken = default) =>
            await ModelContextToolExtensions.WithExceptionCheck(async () =>
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
            var columnDef = GetColumnDefinition(typed.Name, typed.DisplayName ?? typed.Name,
                typed.ColumnType, typed.Choices, choiceSelect);

            return await client.Sites[siteId].Lists[listId].Columns.PostAsync(
                columnDef,
                cancellationToken: cancellationToken
            );
        })));

    [Description("Update a Microsoft List. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Microsoft List", Name = "graph_lists_update_list",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_UpdateList(
        [Description("ID of the SharePoint site.")] string siteId,
        [Description("ID of the Microsoft List.")] string listId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated list display name.")] string? displayName = null,
        [Description("Updated list description.")] string? description = null,
        [Description("Whether list items can have content approval enabled.")] bool? contentTypesEnabled = null,
        [Description("Whether hidden list items are visible in search results.")] bool? hidden = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateSharePointList
                {
                    DisplayName = displayName,
                    Description = description,
                    ContentTypesEnabled = contentTypesEnabled,
                    Hidden = hidden
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(Microsoft.Graph.Beta.Models.List);
            if (input.DisplayName is null && input.Description is null &&
                input.ContentTypesEnabled is null && input.Hidden is null)
                throw new ValidationException("At least one Microsoft List field must be supplied.");

            return await client.Sites[siteId].Lists[listId].PatchAsync(
                new Microsoft.Graph.Beta.Models.List
                {
                    DisplayName = input.DisplayName,
                    Description = input.Description,
                    ListProp = input.ContentTypesEnabled is not null || input.Hidden is not null
                        ? new Microsoft.Graph.Beta.Models.ListInfo
                        {
                            ContentTypesEnabled = input.ContentTypesEnabled,
                            Hidden = input.Hidden
                        }
                        : null
                }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a Microsoft List and all of its items.")]
    [McpServerTool(Title = "Delete Microsoft List", Name = "graph_lists_delete_list",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_DeleteList(
        [Description("ID of the SharePoint site.")] string siteId,
        [Description("ID of the Microsoft List to delete.")] string listId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteSharePointList>(
            listId,
            async _ => await client.Sites[siteId].Lists[listId].DeleteAsync(cancellationToken: cancellationToken),
            "Microsoft List deleted.", cancellationToken));

    [Description("Update an existing column in a Microsoft List. Only supplied values are changed.")]
    [McpServerTool(Title = "Update Microsoft List column", Name = "graph_lists_update_column",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_UpdateColumn(
        [Description("ID of the SharePoint site.")] string siteId,
        [Description("ID of the Microsoft List.")] string listId,
        [Description("ID of the column to update.")] string columnId,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Updated display name.")] string? displayName = null,
        [Description("Updated column description.")] string? description = null,
        [Description("Whether the column is required.")] bool? required = null,
        [Description("Replacement comma-separated choices for an existing choice column.")] string? choices = null,
        [Description("Choice display mode: dropDownMenu, radioButtons, or checkBoxes.")] string? choiceSelect = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new GraphUpdateSharePointColumn
                {
                    DisplayName = displayName,
                    Description = description,
                    Required = required,
                    Choices = choices,
                    ChoiceSelect = choiceSelect
                }, cancellationToken);
            if (notAccepted is not null || input is null) return default(Microsoft.Graph.Beta.Models.ColumnDefinition);
            if (input.DisplayName is null && input.Description is null && input.Required is null &&
                input.Choices is null && input.ChoiceSelect is null)
                throw new ValidationException("At least one Microsoft List column field must be supplied.");

            return await client.Sites[siteId].Lists[listId].Columns[columnId].PatchAsync(
                new Microsoft.Graph.Beta.Models.ColumnDefinition
                {
                    DisplayName = input.DisplayName,
                    Description = input.Description,
                    Required = input.Required,
                    Choice = input.Choices is not null || input.ChoiceSelect is not null
                        ? new Microsoft.Graph.Beta.Models.ChoiceColumn
                        {
                            Choices = input.Choices?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(value => value.Trim()).ToList(),
                            DisplayAs = input.ChoiceSelect
                        }
                        : null
                }, cancellationToken: cancellationToken);
        })));

    [Description("Delete a column from a Microsoft List.")]
    [McpServerTool(Title = "Delete Microsoft List column", Name = "graph_lists_delete_column",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphLists_DeleteColumn(
        [Description("ID of the SharePoint site.")] string siteId,
        [Description("ID of the Microsoft List.")] string listId,
        [Description("ID of the column to delete.")] string columnId,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.ConfirmAndDeleteAsync<GraphDeleteSharePointColumn>(
            columnId,
            async _ => await client.Sites[siteId].Lists[listId].Columns[columnId]
                .DeleteAsync(cancellationToken: cancellationToken),
            "Microsoft List column deleted.", cancellationToken));


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

    [Description("Please fill in the Microsoft List fields to update.")]
    public class GraphUpdateSharePointList
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("contentTypesEnabled")] public bool? ContentTypesEnabled { get; set; }
        [JsonPropertyName("hidden")] public bool? Hidden { get; set; }
    }

    [Description("Please fill in the Microsoft List column fields to update.")]
    public class GraphUpdateSharePointColumn
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("required")] public bool? Required { get; set; }
        [JsonPropertyName("choices")] public string? Choices { get; set; }
        [JsonPropertyName("choiceSelect")] public string? ChoiceSelect { get; set; }
    }

    [Description("Please confirm the Microsoft List ID to delete: {0}")]
    public class GraphDeleteSharePointList : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }

    [Description("Please confirm the Microsoft List column ID to delete: {0}")]
    public class GraphDeleteSharePointColumn : MCPhappey.Common.Models.IHasName
    {
        [Required] public string Name { get; set; } = default!;
    }

    private static Microsoft.Graph.Beta.Models.ColumnDefinition GetColumnDefinition(string name,
        string displayName, SharePointColumnType columnType, string? choices = null,
        string? choiceSelect = null)
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
                    Choices = choices?.Split(',').Select(x => x.Trim()).ToList() ?? [],
                    DisplayAs = choiceSelect ?? "dropDownMenu"
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
