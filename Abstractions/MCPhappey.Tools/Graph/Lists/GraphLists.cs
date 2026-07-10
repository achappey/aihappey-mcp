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
            var columnDef = GetColumnDefinition(typed.Name, typed.DisplayName ?? typed.Name, typed.ColumnType, typed.Choices);

            return await client.Sites[siteId].Lists[listId].Columns.PostAsync(
                columnDef,
                cancellationToken: cancellationToken
            );
        })));

  
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
                    Choices = choices?.Split(',').Select(x => x.Trim()).ToList() ?? []
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
