using System.ComponentModel;
using System.Text;
using ClosedXML.Excel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.ClosedXML;

public static class ClosedXMLPlugin
{
    [Description("Create a new Excel workbook (.xlsx) with a single empty sheet named 'Sheet1'.")]
    [McpServerTool(Name = "closedxml_new_workbook", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> ClosedXML_NewWorkbook(
        [Description("Filename without .xlsx extension")] string fileName,
        [Description("Name of the sheet")] string sheetName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Cell values")] Dictionary<string, string>? values = null,
        [Description("Cell formulas")] Dictionary<string, string>? formulas = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        await using var ms = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add(sheetName);

            foreach (var value in values ?? [])
            {
                worksheet.Cell(value.Key).Value = value.Value;
            }

            foreach (var value in formulas ?? [])
            {
                worksheet.Cell(value.Key).FormulaA1 = value.Value;
            }

            // Save workbook into the memory stream
            workbook.SaveAs(ms);
        }

        // Reset stream position before upload
        ms.Position = 0;

        // Upload to Graph or your storage provider
        var uploaded = await graphClient.Upload(
            $"{fileName}.xlsx",
            await BinaryData.FromStreamAsync(ms, cancellationToken),
            cancellationToken);


        return uploaded?.ToCallToolResult();
    }));

    [Description("Add a new sheet to an existing Excel workbook.")]
    [McpServerTool(Name = "closedxml_add_sheet", ReadOnly = false)]
    public static async Task<CallToolResult?> ClosedXML_AddSheet(
        [Description("File url")] string fileUrl,
        [Description("Name of the new sheet")] string newSheetName,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();

        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        workbook.Worksheets.Add(newSheetName);

        await using var outStream = new MemoryStream();
        workbook.SaveAs(outStream);
        outStream.Position = 0;

        var uploaded = await graphClient.UploadBinaryDataAsync(fileUrl, await BinaryData.FromStreamAsync(outStream, cancellationToken), cancellationToken);
        return uploaded?.ToResourceLinkBlock(uploaded.Name!).ToCallToolResult();
    }));

    [Description("Set or update a single cell value in an existing workbook.")]
    [McpServerTool(Name = "closedxml_set_cell", ReadOnly = false)]
    public static async Task<CallToolResult?> ClosedXML_SetCellValue(
        [Description("File url")] string fileUrl,
        [Description("Sheet name")] string sheetName,
        [Description("Cell address (e.g. A1)")] string cellAddress,
        [Description("New cell value")] string value,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        workbook.Worksheet(sheetName).Cell(cellAddress).Value = value;

        await using var outStream = new MemoryStream();
        workbook.SaveAs(outStream);
        outStream.Position = 0;

        var uploaded = await graphClient.UploadBinaryDataAsync(fileUrl, await BinaryData.FromStreamAsync(outStream, cancellationToken), cancellationToken);
        return uploaded?.ToResourceLinkBlock(uploaded.Name!).ToCallToolResult();
    }));

    [Description("Read a single cell value or formula result from an Excel sheet.")]
    [McpServerTool(Name = "closedxml_get_cell", ReadOnly = true)]
    public static async Task<CallToolResult?> ClosedXML_GetCellValue(
        [Description("File url")] string fileUrl,
        [Description("Sheet name")] string sheetName,
        [Description("Cell address (e.g. B2)")] string cellAddress,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        var value = workbook.Worksheet(sheetName)
            .Cell(cellAddress).Value.ToString();

        return new
        {
            callValuye = value
        };
    }));

    [Description("List all sheet names in an Excel workbook.")]
    [McpServerTool(Name = "closedxml_list_sheets", ReadOnly = true)]
    public static async Task<CallToolResult?> ClosedXML_ListSheets(
        [Description("File url")] string fileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        var sheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();

        return new
        {
            sheetNames
        };
    }));

    [Description("Export a worksheet from an Excel file to CSV format.")]
    [McpServerTool(Name = "closedxml_to_csv", ReadOnly = false)]
    public static async Task<CallToolResult?> ClosedXML_ToCsv(
     [Description("File url of the Excel workbook")] string fileUrl,
     [Description("Name of the sheet to export")] string sheetName,
     [Description("Optional new file name (without extension)")] string? csvFileName,
     RequestContext<CallToolRequestParams> requestContext,
     IServiceProvider serviceProvider,
     CancellationToken cancellationToken = default)
     => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheet(sheetName);

        var sb = new StringBuilder();
        foreach (var row in sheet.RowsUsed())
        {
            // ✅ Explicitly call CellsUsed() as IEnumerable<IXLCell>
            var cellValues = row.CellsUsed().Select(cell =>
            {
                var v = cell.GetFormattedString(); // safer than .Value.ToString()
                return v.Contains(',') ? $"\"{v}\"" : v;
            }).ToList();

            sb.AppendLine(string.Join(",", cellValues));
        }

        var fileName = csvFileName ?? $"{Path.GetFileNameWithoutExtension(fileUrl)}_{sheetName}";
        await using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var uploaded = await graphClient.Upload(
            $"{fileName}.csv",
            await BinaryData.FromStreamAsync(csvStream, cancellationToken),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    [Description("Calculate the numeric sum of a given cell range in an Excel sheet.")]
    [McpServerTool(Name = "closedxml_sum_range", ReadOnly = true)]
    public static async Task<CallToolResult?> ClosedXML_SumRange(
        [Description("File url")] string fileUrl,
        [Description("Sheet name")] string sheetName,
        [Description("Cell range (e.g. A1:A10)")] string rangeAddress,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);

        await using var ms = new MemoryStream(files.First().Contents.ToArray());
        using var workbook = new XLWorkbook(ms);
        var worksheet = workbook.Worksheet(sheetName);
        var range = worksheet.Range(rangeAddress);

        double sum = 0;
        foreach (var cell in range.CellsUsed())
        {
            var cellValue = cell.Value;

            // Handle numeric types explicitly
            if (cellValue.Type == XLDataType.Number)
            {
                sum += cell.GetDouble();
            }
            else if (cellValue.Type == XLDataType.Text && double.TryParse(cellValue.GetText(), out var parsed))
            {
                sum += parsed;
            }
        }

        return new
        {
            result = sum
        };
    }));
}
