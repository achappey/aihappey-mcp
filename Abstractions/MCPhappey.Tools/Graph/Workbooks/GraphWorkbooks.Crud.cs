using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Core.Extensions;
using MCPhappey.Tools.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Workbooks;

public static partial class GraphWorkbooks
{
    [Description("Rename a worksheet in an Excel workbook stored in OneDrive or SharePoint.")]
    [McpServerTool(Title = "Rename Excel worksheet", Name = "graph_workbooks_rename_worksheet",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_RenameWorksheet(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Worksheet ID or current worksheet name.")] string worksheetId,
        [Description("New worksheet name.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new RenameWorksheetInput { Name = name }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);
            ArgumentException.ThrowIfNullOrWhiteSpace(input?.Name);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Patch, $"worksheets/{EscapePath(worksheetId)}",
                new { name = input.Name.Trim() }, cancellationToken);
        })));

    [Description("Delete a worksheet from an Excel workbook stored in OneDrive or SharePoint.")]
    [McpServerTool(Title = "Delete Excel worksheet", Name = "graph_workbooks_delete_worksheet",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_DeleteWorksheet(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Worksheet ID or worksheet name to delete.")] string worksheetId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await requestContext.ConfirmAndDeleteAsync<DeleteWorksheetInput>(worksheetId,
                async _ => await SendWorkbookNoContentAsync(serviceProvider, requestContext, workbook,
                    HttpMethod.Delete, $"worksheets/{EscapePath(worksheetId)}", null, cancellationToken),
                "Excel worksheet deleted.", cancellationToken);
        }));

    [Description("Replace the values in an Excel worksheet range with an explicit two-dimensional JSON array.")]
    [McpServerTool(Title = "Update Excel range values", Name = "graph_workbooks_update_range_values",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_UpdateRangeValues(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Worksheet ID or worksheet name.")] string worksheetId,
        [Description("A1-style range address, for example A1:C3.")] string address,
        [Description("Two-dimensional JSON values array, for example [[\"Name\",\"Count\"],[\"Pens\",4]].")] string valuesJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new UpdateRangeValuesInput { ValuesJson = valuesJson }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);
            var values = ParseMatrix(input?.ValuesJson);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Patch, RangePath(worksheetId, address), new { values }, cancellationToken);
        })));

    [Description("Clear contents, formats, hyperlinks, or all data from an Excel worksheet range.")]
    [McpServerTool(Title = "Clear Excel range", Name = "graph_workbooks_clear_range",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_ClearRange(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Worksheet ID or worksheet name.")] string worksheetId,
        [Description("A1-style range address, for example A1:C3.")] string address,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("What to clear: All, Formats, Contents, Hyperlinks, or RemoveHyperlinks.")] WorkbookRangeClearApplyTo applyTo = WorkbookRangeClearApplyTo.Contents,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new ClearRangeInput { ApplyTo = applyTo }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Post, $"{RangePath(worksheetId, address)}/clear",
                new { applyTo = (input?.ApplyTo ?? applyTo).ToString() }, cancellationToken);
        })));

    [Description("Create an Excel table over an existing worksheet range.")]
    [McpServerTool(Title = "Create Excel table", Name = "graph_workbooks_create_table",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = false, Idempotent = false, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_CreateTable(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Worksheet ID or worksheet name.")] string worksheetId,
        [Description("A1-style source range, for example A1:C20.")] string address,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Whether the first range row contains column headers.")] bool hasHeaders = true,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new CreateTableInput { Address = address, HasHeaders = hasHeaders }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);
            ArgumentException.ThrowIfNullOrWhiteSpace(input?.Address);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Post, $"worksheets/{EscapePath(worksheetId)}/tables/add",
                new { address = input.Address.Trim(), hasHeaders = input.HasHeaders }, cancellationToken);
        })));

    [Description("Rename an existing table in an Excel workbook.")]
    [McpServerTool(Title = "Rename Excel table", Name = "graph_workbooks_rename_table",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_RenameTable(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Table ID or current table name.")] string tableId,
        [Description("New table name.")] string name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new RenameTableInput { Name = name }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);
            ArgumentException.ThrowIfNullOrWhiteSpace(input?.Name);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Patch, $"tables/{EscapePath(tableId)}",
                new { name = input.Name.Trim() }, cancellationToken);
        })));

    [Description("Delete a table from an Excel workbook without deleting the underlying cell values.")]
    [McpServerTool(Title = "Delete Excel table", Name = "graph_workbooks_delete_table",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_DeleteTable(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Table ID or table name to delete.")] string tableId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await requestContext.ConfirmAndDeleteAsync<DeleteTableInput>(tableId,
                async _ => await SendWorkbookNoContentAsync(serviceProvider, requestContext, workbook,
                    HttpMethod.Delete, $"tables/{EscapePath(tableId)}", null, cancellationToken),
                "Excel table deleted; its cell values were retained.", cancellationToken);
        }));

    [Description("Replace the values of one row in an Excel table using a zero-based row index.")]
    [McpServerTool(Title = "Update Excel table row", Name = "graph_workbooks_update_table_row",
        UseStructuredContent = true, OutputSchemaType = typeof(JsonElement),
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_UpdateTableRow(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Table ID or table name.")] string tableId,
        [Description("Zero-based index of the table row.")][Range(0, int.MaxValue)] int rowIndex,
        [Description("JSON array containing the replacement row values, for example [\"Pens\",4].")] string valuesJson,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (input, notAccepted, _) = await requestContext.Server.TryElicit(
                new UpdateTableRowInput { ValuesJson = valuesJson }, cancellationToken);
            ThrowIfNotAccepted(notAccepted);
            var row = ParseRow(input?.ValuesJson);

            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await SendWorkbookJsonAsync(serviceProvider, requestContext, workbook,
                HttpMethod.Patch, $"tables/{EscapePath(tableId)}/rows/itemAt(index={rowIndex})",
                new { values = new[] { row } }, cancellationToken);
        })));

    [Description("Delete one row from an Excel table using a zero-based row index.")]
    [McpServerTool(Title = "Delete Excel table row", Name = "graph_workbooks_delete_table_row",
        Destructive = true, Idempotent = true, OpenWorld = false)]
    public static async Task<CallToolResult?> GraphWorkbooks_DeleteTableRow(
        [Description("OneDrive or SharePoint URL of the Excel workbook.")] string excelFileUrl,
        [Description("Table ID or table name.")] string tableId,
        [Description("Zero-based index of the table row.")][Range(0, int.MaxValue)] int rowIndex,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default) =>
        await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async client =>
        {
            var confirmationName = $"{tableId} row {rowIndex}";
            var workbook = await ResolveWorkbookAsync(client, excelFileUrl, cancellationToken);
            return await requestContext.ConfirmAndDeleteAsync<DeleteTableRowInput>(confirmationName,
                async _ => await SendWorkbookNoContentAsync(serviceProvider, requestContext, workbook,
                    HttpMethod.Delete, $"tables/{EscapePath(tableId)}/rows/itemAt(index={rowIndex})",
                    null, cancellationToken),
                "Excel table row deleted.", cancellationToken);
        }));

    private static async Task<WorkbookReference> ResolveWorkbookAsync(
        Microsoft.Graph.Beta.GraphServiceClient client, string excelFileUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(excelFileUrl);
        var item = await client.GetDriveItem(excelFileUrl, cancellationToken)
            ?? throw new ValidationException("The Excel workbook could not be resolved.");
        var driveId = item.ParentReference?.DriveId;
        if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(item.Id))
            throw new ValidationException("The resolved Excel workbook has no drive ID or item ID.");
        return new WorkbookReference(driveId, item.Id);
    }

    private static async Task<JsonElement> SendWorkbookJsonAsync(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        WorkbookReference workbook, HttpMethod method, string relativePath, object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendWorkbookAsync(serviceProvider, requestContext, workbook,
            method, relativePath, body, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.SerializeToElement(new { success = true });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task SendWorkbookNoContentAsync(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        WorkbookReference workbook, HttpMethod method, string relativePath, object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendWorkbookAsync(serviceProvider, requestContext, workbook,
            method, relativePath, body, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendWorkbookAsync(
        IServiceProvider serviceProvider, RequestContext<CallToolRequestParams> requestContext,
        WorkbookReference workbook, HttpMethod method, string relativePath, object? body,
        CancellationToken cancellationToken)
    {
        var httpClient = await serviceProvider.GetGraphHttpClient(requestContext.Server);
        var requestUri = $"https://graph.microsoft.com/beta/drives/{EscapePath(workbook.DriveId)}/items/{EscapePath(workbook.ItemId)}/workbook/{relativePath}";
        using var request = new HttpRequestMessage(method, requestUri);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, MediaTypeNames.Application.Json);

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        throw new HttpRequestException($"Microsoft Graph workbook request failed ({(int)response.StatusCode} {response.StatusCode}): {error}");
    }

    private static string RangePath(string worksheetId, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worksheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var odataAddress = address.Trim().Replace("'", "''", StringComparison.Ordinal);
        return $"worksheets/{EscapePath(worksheetId)}/range(address='{odataAddress}')";
    }

    private static string EscapePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value.Trim());
    }

    private static JsonElement ParseMatrix(string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Array))
            throw new ValidationException("valuesJson must be a two-dimensional JSON array.");
        return document.RootElement.Clone();
    }

    private static JsonElement ParseRow(string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.Array))
            throw new ValidationException("valuesJson must be a one-dimensional JSON array.");
        return document.RootElement.Clone();
    }

    private static void ThrowIfNotAccepted(object? notAccepted)
    {
        if (notAccepted is not null)
            throw new Exception(JsonSerializer.Serialize(notAccepted));
    }

    private sealed record WorkbookReference(string DriveId, string ItemId);

    [Description("Please review the new Excel worksheet name.")]
    public sealed class RenameWorksheetInput
    {
        [Required, JsonPropertyName("name")]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the Excel worksheet ID or name to delete: {0}")]
    public sealed class DeleteWorksheetInput : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Please review the replacement Excel range values.")]
    public sealed class UpdateRangeValuesInput
    {
        [Required, JsonPropertyName("valuesJson")]
        public string ValuesJson { get; set; } = default!;
    }

    [Description("Please review what should be cleared from the Excel range.")]
    public sealed class ClearRangeInput
    {
        [Required, JsonPropertyName("applyTo")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WorkbookRangeClearApplyTo ApplyTo { get; set; } = WorkbookRangeClearApplyTo.Contents;
    }

    [Description("Please review the new Excel table settings.")]
    public sealed class CreateTableInput
    {
        [Required, JsonPropertyName("address")]
        public string Address { get; set; } = default!;

        [JsonPropertyName("hasHeaders")]
        public bool HasHeaders { get; set; } = true;
    }

    [Description("Please review the new Excel table name.")]
    public sealed class RenameTableInput
    {
        [Required, JsonPropertyName("name")]
        public string Name { get; set; } = default!;
    }

    [Description("Please confirm the Excel table ID or name to delete: {0}")]
    public sealed class DeleteTableInput : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [Description("Please review the replacement Excel table row values.")]
    public sealed class UpdateTableRowInput
    {
        [Required, JsonPropertyName("valuesJson")]
        public string ValuesJson { get; set; } = default!;
    }

    [Description("Please confirm the Excel table and row to delete: {0}")]
    public sealed class DeleteTableRowInput : MCPhappey.Common.Models.IHasName
    {
        [Required]
        public string Name { get; set; } = default!;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkbookRangeClearApplyTo
    {
        All,
        Formats,
        Contents,
        Hyperlinks,
        RemoveHyperlinks
    }
}
