using System.ComponentModel;
using System.Text;
using ClosedXML.Excel;
using MCPhappey.Common.Models;
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
    [McpServerTool(Name = "closedxml_new_workbook",
        ReadOnly = false,
        OpenWorld = false,
        Idempotent = false,
        Destructive = false)]
    public static async Task<CallToolResult?> ClosedXML_NewWorkbook(
        [Description("Filename without .xlsx extension")] string fileName,
        [Description("Name of the sheet")] string sheetName,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Cell values")] Dictionary<string, string>? values = null,
        [Description("Cell formulas")] Dictionary<string, string>? formulas = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
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
            cancellationToken)
            ?? throw new IOException("ClosedXML workbook upload failed.");


        return uploaded.ToCallToolResult();
    }));

    [Description("Add a new sheet to an existing Excel workbook.")]
    [McpServerTool(Name = "closedxml_add_sheet", ReadOnly = false)]
    public static async Task<CallToolResult?> ClosedXML_AddSheet(
        [Description("File url")] string fileUrl,
        [Description("Name of the new sheet")] string newSheetName,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
           await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var snapshot = await graphClient.DownloadOfficeDocumentAsync(fileUrl, cancellationToken);
        await using var ms = new MemoryStream(snapshot.Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        workbook.Worksheets.Add(newSheetName);

        await using var outStream = new MemoryStream();
        workbook.SaveAs(outStream);
        outStream.Position = 0;

        var uploaded = await graphClient.UploadOfficeDocumentAsync(snapshot.Item, await BinaryData.FromStreamAsync(outStream, cancellationToken), cancellationToken)
            ?? throw new IOException($"ClosedXML failed to update workbook at {fileUrl}.");

        return uploaded.ToResourceLinkBlock(uploaded.Name ?? newSheetName).ToCallToolResult();
    }));

    [Description("Update cell values or formulas, or patch formatting without changing content. An optional endpoint selects a rectangular range of at most 10,000 cells.")]
    [McpServerTool(Name = "closedxml_set_cell", ReadOnly = false)]
    public static async Task<CallToolResult?> ClosedXML_SetCellValue(
        [Description("File url")] string fileUrl,
        [Description("Sheet name")] string sheetName,
        [Description("Cell address (e.g. A1)")] string cellAddress,
        [Description("New cell value")] string value,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        [Description("value (default), formula, or formatOnly. formatOnly ignores value and formula.")] string operation = "value",
        [Description("Optional last cell of a rectangular range, e.g. C5. Maximum 10,000 cells.")] string? endCellAddress = null,
        [Description("Partial formatting patch; omitted properties keep their current settings.")] ClosedXmlFormattingPatch? formatting = null,
        [Description("Formula in A1 notation, required for the formula operation.")] string? formula = null)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var snapshot = await graphClient.DownloadOfficeDocumentAsync(fileUrl, cancellationToken);
        await using var ms = new MemoryStream(snapshot.Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        EditCells(workbook.Worksheet(sheetName), cellAddress, value, operation, endCellAddress, formatting, formula);

        await using var outStream = new MemoryStream();
        workbook.SaveAs(outStream);
        outStream.Position = 0;

        var uploaded = await graphClient.UploadOfficeDocumentAsync(snapshot.Item, await BinaryData.FromStreamAsync(outStream, cancellationToken), cancellationToken)
            ?? throw new IOException($"ClosedXML failed to update workbook at {fileUrl}.");

        return uploaded.ToResourceLinkBlock(uploaded.Name ?? Path.GetFileName(fileUrl)).ToCallToolResult();
    }));

    [Description("Inspect cell values, formulas, formatting, dimensions, merges, and conditional rule summaries for a cell or rectangular range of at most 10,000 cells.")]
    [McpServerTool(Name = "closedxml_get_cell", ReadOnly = true)]
    public static async Task<CallToolResult?> ClosedXML_GetCellValue(
        [Description("File url")] string fileUrl,
        [Description("Sheet name")] string sheetName,
        [Description("Cell address (e.g. B2)")] string cellAddress,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        [Description("Optional last cell of a rectangular range, e.g. C5. Maximum 10,000 cells.")] string? endCellAddress = null)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = GetRequiredDownloadedFile(files, fileUrl, "workbook");
        await using var ms = new MemoryStream(file.Contents.ToArray());
        using var workbook = new XLWorkbook(ms);

        return InspectCells(workbook.Worksheet(sheetName), cellAddress, endCellAddress);
    }));

    [Description("List all sheet names in an Excel workbook.")]
    [McpServerTool(Name = "closedxml_list_sheets", ReadOnly = true)]
    public static async Task<CallToolResult?> ClosedXML_ListSheets(
        [Description("File url")] string fileUrl,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = GetRequiredDownloadedFile(files, fileUrl, "workbook");
        await using var ms = new MemoryStream(file.Contents.ToArray());
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
     => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = GetRequiredDownloadedFile(files, fileUrl, "workbook");
        await using var ms = new MemoryStream(file.Contents.ToArray());
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
            cancellationToken)
            ?? throw new IOException("ClosedXML CSV upload failed.");

        return uploaded.ToCallToolResult();
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
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var file = GetRequiredDownloadedFile(files, fileUrl, "workbook");

        await using var ms = new MemoryStream(file.Contents.ToArray());
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

    internal static void EditCells(IXLWorksheet sheet, string cellAddress, string value,
        string operation = "value", string? endCellAddress = null,
        ClosedXmlFormattingPatch? formatting = null, string? formula = null)
    {
        if (operation is not ("value" or "formula" or "formatOnly"))
            throw new ArgumentException("Operation must be value, formula, or formatOnly.", nameof(operation));
        if (operation == "formula" && string.IsNullOrWhiteSpace(formula))
            throw new ArgumentException("A nonempty formula is required for the formula operation.", nameof(formula));

        var range = GetBoundedRange(sheet, cellAddress, endCellAddress);
        foreach (var cell in range.Cells())
        {
            if (operation == "value")
            {
                var originalStyle = cell.Style;
                cell.Value = value;
                // ClosedXML can infer wrapping/quote-prefix formatting from text.
                cell.Style = originalStyle;
            }
            else if (operation == "formula")
                cell.FormulaA1 = formula!;

            // Patch each cell independently so heterogeneous range styles remain intact.
            formatting?.Apply(cell.Style);
        }
    }

    internal static object InspectCells(IXLWorksheet sheet, string cellAddress, string? endCellAddress = null)
    {
        var range = GetBoundedRange(sheet, cellAddress, endCellAddress);
        var first = range.FirstCell();
        return new
        {
            callValuye = first.Value.ToString(),
            value = first.Value.ToString(),
            formula = first.HasFormula ? first.FormulaA1 : null,
            range = range.RangeAddress.ToString(),
            cells = range.Cells().Select(cell => new
            {
                address = cell.Address.ToString(),
                value = cell.Value.ToString(),
                formula = cell.HasFormula ? cell.FormulaA1 : null,
                dataType = cell.DataType.ToString(),
                style = DescribeStyle(cell.Style)
            }).ToArray(),
            rows = range.Rows().Select(row => new
            {
                number = row.RowNumber(),
                height = sheet.Row(row.RowNumber()).Height,
                hidden = sheet.Row(row.RowNumber()).IsHidden
            }).ToArray(),
            columns = range.Columns().Select(column => new
            {
                number = column.ColumnNumber(),
                width = sheet.Column(column.ColumnNumber()).Width,
                hidden = sheet.Column(column.ColumnNumber()).IsHidden
            }).ToArray(),
            merges = sheet.MergedRanges.Where(merged => merged.Intersects(range))
                .Select(merged => merged.RangeAddress.ToString()).ToArray(),
            conditionalRules = sheet.ConditionalFormats
                .Where(rule => rule.Ranges.Any(appliesTo => appliesTo.Intersects(range)))
                .Select(rule => new
                {
                    type = rule.ConditionalFormatType.ToString(),
                    ranges = rule.Ranges.Select(appliesTo => appliesTo.RangeAddress.ToString()).ToArray(),
                    stopIfTrue = rule.StopIfTrue
                }).ToArray()
        };
    }

    private static IXLRange GetBoundedRange(IXLWorksheet sheet, string cellAddress, string? endCellAddress)
    {
        var range = sheet.Range(cellAddress, endCellAddress ?? cellAddress);
        if ((long)range.RowCount() * range.ColumnCount() > 10_000)
            throw new ArgumentException("The selected range must contain at most 10,000 cells.", nameof(endCellAddress));
        return range;
    }

    private static object DescribeStyle(IXLStyle style) => new
    {
        font = new
        {
            name = style.Font.FontName, size = style.Font.FontSize,
            bold = style.Font.Bold, italic = style.Font.Italic,
            underline = style.Font.Underline.ToString(), strikethrough = style.Font.Strikethrough,
            color = style.Font.FontColor.ToString()
        },
        fill = new
        {
            pattern = style.Fill.PatternType.ToString(),
            backgroundColor = style.Fill.BackgroundColor.ToString(),
            patternColor = style.Fill.PatternColor.ToString()
        },
        border = new
        {
            top = style.Border.TopBorder.ToString(), topColor = style.Border.TopBorderColor.ToString(),
            bottom = style.Border.BottomBorder.ToString(), bottomColor = style.Border.BottomBorderColor.ToString(),
            left = style.Border.LeftBorder.ToString(), leftColor = style.Border.LeftBorderColor.ToString(),
            right = style.Border.RightBorder.ToString(), rightColor = style.Border.RightBorderColor.ToString()
        },
        alignment = new
        {
            horizontal = style.Alignment.Horizontal.ToString(), vertical = style.Alignment.Vertical.ToString(),
            wrapText = style.Alignment.WrapText, textRotation = style.Alignment.TextRotation,
            indent = style.Alignment.Indent, shrinkToFit = style.Alignment.ShrinkToFit
        },
        numberFormat = new { id = style.NumberFormat.NumberFormatId, format = style.NumberFormat.Format }
    };

    private static FileItem GetRequiredDownloadedFile(IEnumerable<FileItem> files, string fileUrl, string fileKind)
        => files.FirstOrDefault()
            ?? throw new FileNotFoundException($"No {fileKind} found at {fileUrl}");
}

public sealed class ClosedXmlFormattingPatch
{
    public ClosedXmlFontPatch? Font { get; set; }
    public ClosedXmlFillPatch? Fill { get; set; }
    public ClosedXmlBorderPatch? Border { get; set; }
    public ClosedXmlAlignmentPatch? Alignment { get; set; }
    [Description("Custom Excel number format code. Takes precedence over NumberFormatId.")]
    public string? NumberFormat { get; set; }
    public int? NumberFormatId { get; set; }

    internal void Apply(IXLStyle style)
    {
        Font?.Apply(style.Font);
        Fill?.Apply(style.Fill);
        Border?.Apply(style.Border);
        Alignment?.Apply(style.Alignment);
        if (NumberFormatId is { } id) style.NumberFormat.NumberFormatId = id;
        if (NumberFormat is { } format) style.NumberFormat.Format = format;
    }
}

public sealed class ClosedXmlFontPatch
{
    public string? Name { get; set; }
    public double? Size { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Strikethrough { get; set; }
    public XLFontUnderlineValues? Underline { get; set; }
    [Description("HTML color, e.g. #FF0000.")]
    public string? Color { get; set; }

    internal void Apply(IXLFont font)
    {
        if (Name is { } name) font.FontName = name;
        if (Size is { } size) font.FontSize = size;
        if (Bold is { } bold) font.Bold = bold;
        if (Italic is { } italic) font.Italic = italic;
        if (Strikethrough is { } strike) font.Strikethrough = strike;
        if (Underline is { } underline) font.Underline = underline;
        if (Color is { } color) font.FontColor = XLColor.FromHtml(color);
    }
}

public sealed class ClosedXmlFillPatch
{
    public XLFillPatternValues? Pattern { get; set; }
    [Description("HTML background color, e.g. #FFFF00.")]
    public string? BackgroundColor { get; set; }
    [Description("HTML pattern color, e.g. #000000.")]
    public string? PatternColor { get; set; }

    internal void Apply(IXLFill fill)
    {
        if (BackgroundColor is { } background) fill.BackgroundColor = XLColor.FromHtml(background);
        if (PatternColor is { } color) fill.PatternColor = XLColor.FromHtml(color);
        if (Pattern is { } pattern) fill.PatternType = pattern;
    }
}

public sealed class ClosedXmlBorderPatch
{
    public XLBorderStyleValues? Top { get; set; }
    public XLBorderStyleValues? Bottom { get; set; }
    public XLBorderStyleValues? Left { get; set; }
    public XLBorderStyleValues? Right { get; set; }
    [Description("HTML color applied to all four border sides.")]
    public string? Color { get; set; }

    internal void Apply(IXLBorder border)
    {
        if (Top is { } top) border.TopBorder = top;
        if (Bottom is { } bottom) border.BottomBorder = bottom;
        if (Left is { } left) border.LeftBorder = left;
        if (Right is { } right) border.RightBorder = right;
        if (Color is { } color)
        {
            var nativeColor = XLColor.FromHtml(color);
            border.TopBorderColor = nativeColor;
            border.BottomBorderColor = nativeColor;
            border.LeftBorderColor = nativeColor;
            border.RightBorderColor = nativeColor;
        }
    }
}

public sealed class ClosedXmlAlignmentPatch
{
    public XLAlignmentHorizontalValues? Horizontal { get; set; }
    public XLAlignmentVerticalValues? Vertical { get; set; }
    public bool? WrapText { get; set; }
    public bool? ShrinkToFit { get; set; }
    public int? TextRotation { get; set; }
    public int? Indent { get; set; }

    internal void Apply(IXLAlignment alignment)
    {
        if (Horizontal is { } horizontal) alignment.Horizontal = horizontal;
        if (Vertical is { } vertical) alignment.Vertical = vertical;
        if (WrapText is { } wrap) alignment.WrapText = wrap;
        if (ShrinkToFit is { } shrink) alignment.ShrinkToFit = shrink;
        if (TextRotation is { } rotation) alignment.TextRotation = rotation;
        if (Indent is { } indent) alignment.Indent = indent;
    }
}
