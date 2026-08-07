using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Workbooks;

public static partial class GraphWorkbooks
{
    [Description("Create a worksheet-scoped Excel named item that refers to a range or formula.")]
    [McpServerTool(Title = "Create worksheet named item", Name = "graph_workbooks_create_worksheet_named_item",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> CreateWorksheetNamedItem(
        string excelFileUrl, string worksheetId, string name, string reference,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        string? comment = null, CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new CreateNamedItemInput { Name = name, Reference = reference, Comment = comment }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            ArgumentException.ThrowIfNullOrWhiteSpace(input?.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(input.Reference);
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Post,
                $"worksheets/{EscapePath(worksheetId)}/names/add",
                new { name = input.Name.Trim(), reference = input.Reference.Trim(), comment = input.Comment }, cancellationToken);
        })));

    [Description("Update a worksheet's visibility or zero-based position in an Excel workbook.")]
    [McpServerTool(Title = "Update Excel worksheet properties", Name = "graph_workbooks_update_worksheet_properties",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> UpdateWorksheetProperties(
        string excelFileUrl, string worksheetId, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        WorkbookWorksheetVisibility? visibility = null, [Range(0, int.MaxValue)] int? position = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (visibility is null && position is null) throw new ValidationException("A visibility or position is required.");
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new WorksheetPropertiesInput { Visibility = visibility, Position = position }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            var body = new Dictionary<string, object?>();
            if (input?.Visibility is not null) body["visibility"] = input.Visibility.ToString();
            if (input?.Position is not null) body["position"] = input.Position;
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Patch,
                $"worksheets/{EscapePath(worksheetId)}", body, cancellationToken);
        })));

    [Description("Replace formulas in an Excel worksheet range with a two-dimensional JSON array.")]
    [McpServerTool(Title = "Update Excel range formulas", Name = "graph_workbooks_update_range_formulas",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> UpdateRangeFormulas(
        string excelFileUrl, string worksheetId, string address, string formulasJson,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await PatchMatrixProperty(excelFileUrl, worksheetId, address, formulasJson, "formulas",
            serviceProvider, requestContext, cancellationToken);

    [Description("Update basic formatting for an Excel worksheet range.")]
    [McpServerTool(Title = "Update Excel range format", Name = "graph_workbooks_update_range_format",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> UpdateRangeFormat(
        string excelFileUrl, string worksheetId, string address, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, string? fillColor = null,
        string? fontColor = null, bool? fontBold = null, double? fontSize = null,
        string? horizontalAlignment = null, string? verticalAlignment = null,
        bool? wrapText = null, string? numberFormatJson = null,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            if (fillColor is null && fontColor is null && fontBold is null && fontSize is null
                && horizontalAlignment is null && verticalAlignment is null && wrapText is null && numberFormatJson is null)
                throw new ValidationException("At least one range format property is required.");
            var (input, rejected, _) = await requestContext.Server.TryElicit(new RangeFormatInput
            {
                FillColor = fillColor, FontColor = fontColor, FontBold = fontBold, FontSize = fontSize,
                HorizontalAlignment = horizontalAlignment, VerticalAlignment = verticalAlignment,
                WrapText = wrapText, NumberFormatJson = numberFormatJson
            }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            var body = new Dictionary<string, object?>();
            if (input?.FillColor is not null) body["fill"] = new { color = input.FillColor };
            var font = new Dictionary<string, object?>();
            if (input?.FontColor is not null) font["color"] = input.FontColor;
            if (input?.FontBold is not null) font["bold"] = input.FontBold;
            if (input?.FontSize is not null) font["size"] = input.FontSize;
            if (font.Count > 0) body["font"] = font;
            if (input?.HorizontalAlignment is not null) body["horizontalAlignment"] = input.HorizontalAlignment;
            if (input?.VerticalAlignment is not null) body["verticalAlignment"] = input.VerticalAlignment;
            if (input?.WrapText is not null) body["wrapText"] = input.WrapText;
            if (input?.NumberFormatJson is not null) body["numberFormat"] = ParseMatrix(input.NumberFormatJson);
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Patch,
                $"{RangePath(worksheetId, address)}/format", body, cancellationToken);
        })));

    [Description("Apply a sort to an Excel table using zero-based column indexes.")]
    [McpServerTool(Title = "Sort Excel table", Name = "graph_workbooks_sort_table",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> SortTable(
        string excelFileUrl, string tableId, string fieldsJson, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, bool matchCase = false,
        string? method = null, CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new TableSortInput { FieldsJson = fieldsJson, MatchCase = matchCase, Method = method }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            var fields = ParseArray(input?.FieldsJson);
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Post,
                $"tables/{EscapePath(tableId)}/sort/apply",
                new { fields, matchCase = input!.MatchCase, method = input.Method }, cancellationToken);
        })));

    [Description("Clear the current sort from an Excel table.")]
    [McpServerTool(Title = "Clear Excel table sort", Name = "graph_workbooks_clear_table_sort",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> ClearTableSort(
        string excelFileUrl, string tableId, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default) =>
        await ExecuteSimplePost(excelFileUrl, $"tables/{EscapePath(tableId)}/sort/clear",
            serviceProvider, requestContext, cancellationToken);

    [Description("Apply a values filter to one Excel table column.")]
    [McpServerTool(Title = "Apply Excel table-column values filter", Name = "graph_workbooks_apply_table_column_filter",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true, OpenWorld = false)]
    public static async Task<CallToolResult?> ApplyTableColumnFilter(
        string excelFileUrl, string tableId, string columnId, string valuesJson,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new FilterValuesInput { ValuesJson = valuesJson }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            var values = ParseArray(input?.ValuesJson);
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Post,
                $"tables/{EscapePath(tableId)}/columns/{EscapePath(columnId)}/filter/applyValuesFilter",
                new { values }, cancellationToken);
        })));

    [Description("Clear the filter from one Excel table column.")]
    [McpServerTool(Title = "Clear Excel table-column filter", Name = "graph_workbooks_clear_table_column_filter",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement), Destructive = true,
        Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> ClearTableColumnFilter(
        string excelFileUrl, string tableId, string columnId, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken = default) =>
        await ExecuteSimplePost(excelFileUrl,
            $"tables/{EscapePath(tableId)}/columns/{EscapePath(columnId)}/filter/clear",
            serviceProvider, requestContext, cancellationToken);

    private static async Task<CallToolResult?> PatchMatrixProperty(string excelFileUrl, string worksheetId,
        string address, string json, string propertyName, IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext, CancellationToken cancellationToken) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, rejected, _) = await requestContext.Server.TryElicit(
                new MatrixInput { Json = json }, cancellationToken);
            ThrowIfNotAccepted(rejected);
            var matrix = ParseMatrix(input?.Json);
            var body = new Dictionary<string, object?> { [propertyName] = matrix };
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Patch,
                RangePath(worksheetId, address), body, cancellationToken);
        })));

    private static async Task<CallToolResult?> ExecuteSimplePost(string excelFileUrl, string relativePath,
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook, HttpMethod.Post,
                relativePath, new { }, cancellationToken);
        })));

    private static JsonElement ParseArray(string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new ValidationException("The JSON value must be an array.");
        return document.RootElement.Clone();
    }

    [Description("Review the worksheet properties.")]
    public sealed class WorksheetPropertiesInput
    {
        [JsonPropertyName("visibility"), JsonConverter(typeof(JsonStringEnumConverter))]
        public WorkbookWorksheetVisibility? Visibility { get; set; }
        [JsonPropertyName("position"), Range(0, int.MaxValue)] public int? Position { get; set; }
    }
    [Description("Review the range formulas matrix.")]
    public sealed class MatrixInput
    { [Required, JsonPropertyName("json")] public string Json { get; set; } = default!; }
    [Description("Review the Excel range formatting.")]
    public sealed class RangeFormatInput
    {
        [JsonPropertyName("fillColor")] public string? FillColor { get; set; }
        [JsonPropertyName("fontColor")] public string? FontColor { get; set; }
        [JsonPropertyName("fontBold")] public bool? FontBold { get; set; }
        [JsonPropertyName("fontSize")] public double? FontSize { get; set; }
        [JsonPropertyName("horizontalAlignment")] public string? HorizontalAlignment { get; set; }
        [JsonPropertyName("verticalAlignment")] public string? VerticalAlignment { get; set; }
        [JsonPropertyName("wrapText")] public bool? WrapText { get; set; }
        [JsonPropertyName("numberFormatJson")] public string? NumberFormatJson { get; set; }
    }
    [Description("Review the Excel table sort fields.")]
    public sealed class TableSortInput
    {
        [Required, JsonPropertyName("fieldsJson")] public string FieldsJson { get; set; } = default!;
        [JsonPropertyName("matchCase")] public bool MatchCase { get; set; }
        [JsonPropertyName("method")] public string? Method { get; set; }
    }
    [Description("Review the values for the Excel table-column filter.")]
    public sealed class FilterValuesInput
    { [Required, JsonPropertyName("valuesJson")] public string ValuesJson { get; set; } = default!; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkbookWorksheetVisibility { Visible, Hidden, VeryHidden }
}
