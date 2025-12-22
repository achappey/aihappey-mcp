using System.ComponentModel;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.OpenXML.Excel;

public static class ExcelPlugin
{
    // -----------------------------
    // BASIC, FULLY-WORKING EXCEL TOOLS
    // -----------------------------

    [Description("Create a new Excel workbook (.xlsx) with a single empty sheet named 'Sheet1'.")]
    [McpServerTool(Name = "openxml_excel_new_workbook", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLExcel_NewWorkbook(
        [Description("Filename without .xlsx extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var safe = SanitizeFileName(fileName);
        using var ms = new MemoryStream();
        CreateWorkbook(ms, "Sheet1");
        ms.Position = 0;

        var uploaded = await graphClient.Upload(
            $"{safe}.xlsx",
            await BinaryData.FromStreamAsync(ms, cancellationToken),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    [Description("Add a new empty worksheet to an existing workbook. If sheetName exists, a unique suffix is appended.")]
    [McpServerTool(Name = "openxml_excel_add_worksheet", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLExcel_AddWorksheet(
        [Description("Target workbook URL (.xlsx)")] string url,
        [Description("Desired sheet name")] string sheetName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var download = serviceProvider.GetRequiredService<DownloadService>();
        var files = await download.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No workbook found at {url}");

        using var ms = new MemoryStream(file.Contents.ToArray());
        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wb = doc.WorkbookPart ?? throw new InvalidDataException("Missing WorkbookPart");
            var sheets = wb.Workbook.Sheets ??= new Sheets();

            // Ensure unique name
            var finalName = EnsureUniqueSheetName(sheets, sheetName);

            // Create worksheet
            var wsp = wb.AddNewPart<WorksheetPart>();
            wsp.Worksheet = new Worksheet(new SheetData());
            wsp.Worksheet.Save();

            // Next sheet id
            uint nextId = sheets.Elements<Sheet>().Select(s => s.SheetId?.Value ?? 0U).DefaultIfEmpty(0U).Max() + 1U;
            var relId = wb.GetIdOfPart(wsp);
            sheets.Append(new Sheet { Name = finalName, SheetId = nextId, Id = relId });
            wb.Workbook.Save();
        }

        var updated = await graphClient.UploadBinaryDataAsync(url, new BinaryData(ms.ToArray()), cancellationToken);
        return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));

    [Description("Set a single cell value by A1 address. Types: string | number | bool | date (stored as text).")]
    [McpServerTool(Name = "openxml_excel_set_cell", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLExcel_SetCell(
        [Description("Target workbook URL (.xlsx)")] string url,
        [Description("Sheet selector: name OR zero-based index as string (e.g., '0', 'Sheet1')")] string sheet,
        [Description("Cell address in A1 notation (e.g., B2)")] string address,
        [Description("Value to write")] string value,
        [Description("Value type: string | number | bool | date")] string type,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var download = serviceProvider.GetRequiredService<DownloadService>();
        var files = await download.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No workbook found at {url}");

        using var ms = new MemoryStream(file.Contents.ToArray());
        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wb = doc.WorkbookPart ?? throw new InvalidDataException("Missing WorkbookPart");
            var (wsp, _sheet) = GetWorksheetPartByNameOrIndex(wb, sheet);
            var cell = InsertCellInWorksheet(wsp, address);

            switch ((type ?? "string").Trim().ToLowerInvariant())
            {
                case "number":
                    cell.CellValue = new CellValue(value);
                    cell.DataType = CellValues.Number;
                    break;
                case "bool":
                case "boolean":
                    cell.CellValue = new CellValue((value?.Trim().ToLowerInvariant()) switch
                    {
                        "true" or "1" or "yes" => "1",
                        _ => "0"
                    });
                    cell.DataType = CellValues.Boolean;
                    break;
                case "date":
                    // store as text (basic, no styles)
                    SetSharedString(wb, cell, value);
                    break;
                default:
                    SetSharedString(wb, cell, value);
                    break;
            }

            wsp.Worksheet.Save();
            wb.Workbook.Save();
        }

        var updated = await graphClient.UploadBinaryDataAsync(url, new BinaryData(ms.ToArray()), cancellationToken);
        return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));

    [Description("Import CSV rows into a worksheet. If clearBefore=true, the sheet is emptied first; otherwise rows are appended.")]
    [McpServerTool(Name = "openxml_excel_import_csv", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = true)]
    public static async Task<CallToolResult?> OpenXMLExcel_ImportCsv(
        [Description("Target workbook URL (.xlsx)")] string url,
        [Description("Sheet selector: name OR zero-based index as string (e.g., '0', 'Sheet1')")] string sheet,
        [Description("CSV file URL to import")] string csvUrl,
        [Description("If true, clears the sheet before import; else appends rows")] bool clearBefore,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var download = serviceProvider.GetRequiredService<DownloadService>();

        // download workbook
        var wbFiles = await download.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var wbFile = wbFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No workbook found at {url}");

        // download CSV
        var csvFiles = await download.DownloadContentAsync(serviceProvider, requestContext.Server, csvUrl, cancellationToken);
        var csvFile = csvFiles.FirstOrDefault() ?? throw new FileNotFoundException($"No CSV found at {csvUrl}");
        var csvText = Encoding.UTF8.GetString(csvFile.Contents.ToArray());
        var rows = ParseCsv(csvText);

        using var ms = new MemoryStream(wbFile.Contents.ToArray());
        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wb = doc.WorkbookPart ?? throw new InvalidDataException("Missing WorkbookPart");
            var (wsp, _sheet) = GetWorksheetPartByNameOrIndex(wb, sheet);
            var sheetData = wsp.Worksheet.GetFirstChild<SheetData>() ?? wsp.Worksheet.AppendChild(new SheetData());

            if (clearBefore)
            {
                wsp.Worksheet.RemoveAllChildren<SheetData>();
                sheetData = wsp.Worksheet.AppendChild(new SheetData());
            }

            // append after last used row
            uint startRow = sheetData.Elements<Row>().Select(r => r.RowIndex?.Value ?? 0U).DefaultIfEmpty(0U).Max() + 1U;

            var sst = GetOrCreateSharedStringTable(wb);

            for (int r = 0; r < rows.Count; r++)
            {
                var row = new Row { RowIndex = startRow + (uint)r };
                sheetData.Append(row);

                for (int c = 0; c < rows[r].Count; c++)
                {
                    string colName = ColumnNameFromIndex(c + 1);
                    string a1 = colName + (startRow + (uint)r).ToString();
                    var cell = new Cell { CellReference = a1 };
                    row.Append(cell);

                    // store all CSV as shared strings to be safe
                    var text = rows[r][c] ?? string.Empty;
                    var idx = InsertSharedStringItem(sst, text);
                    cell.CellValue = new CellValue(idx.ToString());
                    cell.DataType = CellValues.SharedString;
                }
            }

            wsp.Worksheet.Save();
            wb.Workbook.Save();
        }

        var updated = await graphClient.UploadBinaryDataAsync(url, new BinaryData(ms.ToArray()), cancellationToken);
        return updated?.ToResourceLinkBlock(updated?.Name!).ToCallToolResult();
    }));

    [Description("Export a worksheet to CSV and upload it as a new .csv file.")]
    [McpServerTool(Name = "openxml_excel_export_csv", ReadOnly = false, OpenWorld = false, Idempotent = false, Destructive = false)]
    public static async Task<CallToolResult?> OpenXMLExcel_ExportCsv(
        [Description("Source workbook URL (.xlsx)")] string url,
        [Description("Sheet selector: name OR zero-based index as string (e.g., '0', 'Sheet1')")] string sheet,
        [Description("Target CSV filename without extension")] string fileName,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async (graphClient) =>
    {
        var download = serviceProvider.GetRequiredService<DownloadService>();
        var files = await download.DownloadContentAsync(serviceProvider, requestContext.Server, url, cancellationToken);
        var file = files.FirstOrDefault() ?? throw new FileNotFoundException($"No workbook found at {url}");

        List<List<string>> table;
        using (var ms = new MemoryStream(file.Contents.ToArray()))
        using (var doc = SpreadsheetDocument.Open(ms, false))
        {
            var wb = doc.WorkbookPart ?? throw new InvalidDataException("Missing WorkbookPart");
            var (wsp, _sheet) = GetWorksheetPartByNameOrIndex(wb, sheet);
            table = ReadSheetAsTable(wb, wsp);
        }

        var csv = ToCsv(table);
        var safe = SanitizeFileName(fileName);
        var uploaded = await graphClient.Upload(
            $"{safe}.csv",
            BinaryData.FromString(csv),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    // -----------------------------
    // Helpers
    // -----------------------------

    private static void CreateWorkbook(Stream stream, string firstSheetName)
    {
        using var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new Workbook();

        var wsp = wb.AddNewPart<WorksheetPart>();
        wsp.Worksheet = new Worksheet(new SheetData());
        wsp.Worksheet.Save();

        wb.Workbook.Sheets = new Sheets();
        var relId = wb.GetIdOfPart(wsp);
        wb.Workbook.Sheets.Append(new Sheet { Name = firstSheetName, SheetId = 1U, Id = relId });
        wb.Workbook.Save();
    }

    private static (WorksheetPart wsp, Sheet sheet) GetWorksheetPartByNameOrIndex(WorkbookPart wb, string selector)
    {
        var sheets = wb.Workbook.Sheets ?? throw new InvalidDataException("Workbook has no sheets");

        Sheet? sheet = null;
        if (int.TryParse((selector ?? string.Empty).Trim(), out var idx) && idx >= 0)
        {
            sheet = sheets.Elements<Sheet>().Skip(idx).FirstOrDefault();
        }
        else
        {
            sheet = sheets.Elements<Sheet>().FirstOrDefault(s => string.Equals(s.Name?.Value, selector, StringComparison.OrdinalIgnoreCase));
        }

        sheet ??= sheets.Elements<Sheet>().FirstOrDefault() ?? throw new InvalidDataException("No worksheet found");
        var wsp = (WorksheetPart)wb.GetPartById(sheet.Id!);
        return (wsp, sheet);
    }

    private static Cell InsertCellInWorksheet(WorksheetPart worksheetPart, string addressName)
    {
        var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>() ?? worksheetPart.Worksheet.AppendChild(new SheetData());

        var (col, rowIndex) = SplitAddress(addressName);
        var row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == rowIndex);
        if (row == null)
        {
            row = new Row { RowIndex = rowIndex };
            // keep rows ordered
            var refRow = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value! > rowIndex);
            if (refRow != null) sheetData.InsertBefore(row, refRow); else sheetData.Append(row);
        }

        // find existing cell
        var cell = row.Elements<Cell>().FirstOrDefault(c => string.Equals(c.CellReference?.Value, addressName, StringComparison.OrdinalIgnoreCase));
        if (cell != null) return cell;

        // Insert in correct column order
        Cell? refCell = null;
        foreach (var c in row.Elements<Cell>())
        {
            var (cCol, _) = SplitAddress(c.CellReference!.Value!);
            if (CompareColumnNames(cCol, col) > 0)
            {
                refCell = c; break;
            }
        }

        cell = new Cell { CellReference = addressName };
        if (refCell != null) row.InsertBefore(cell, refCell); else row.Append(cell);
        return cell;
    }

    private static int CompareColumnNames(string a, string b)
    {
        return ColumnNameToIndex(a).CompareTo(ColumnNameToIndex(b));
    }

    private static (string col, uint row) SplitAddress(string a1)
    {
        if (string.IsNullOrWhiteSpace(a1)) throw new ArgumentException("Invalid address", nameof(a1));
        var col = new string(a1.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var rowStr = new string(a1.SkipWhile(char.IsLetter).ToArray());
        if (string.IsNullOrEmpty(col) || !uint.TryParse(rowStr, out uint r))
            throw new ArgumentException($"Invalid A1 address '{a1}'", nameof(a1));
        return (col, r);
    }

    private static void SetSharedString(WorkbookPart wb, Cell cell, string? value)
    {
        var sst = GetOrCreateSharedStringTable(wb);
        var idx = InsertSharedStringItem(sst, value ?? string.Empty);
        cell.CellValue = new CellValue(idx.ToString());
        cell.DataType = CellValues.SharedString;
    }

    private static SharedStringTable GetOrCreateSharedStringTable(WorkbookPart wb)
    {
        var part = wb.SharedStringTablePart ?? wb.AddNewPart<SharedStringTablePart>();
        part.SharedStringTable ??= new SharedStringTable();
        return part.SharedStringTable;
    }

    private static int InsertSharedStringItem(SharedStringTable sst, string text)
    {
        int i = 0;
        foreach (SharedStringItem item in sst.Elements<SharedStringItem>())
        {
            if (item.Text?.Text == text) return i;
            i++;
        }

        sst.AppendChild(new SharedStringItem(new Text(text)));
        sst.Save();
        return i;
    }

    private static string ColumnNameFromIndex(int index)
    {
        // 1 -> A, 2 -> B, ..., 27 -> AA
        var sb = new StringBuilder();
        while (index > 0)
        {
            index--;
            sb.Insert(0, (char)('A' + (index % 26)));
            index /= 26;
        }
        return sb.ToString();
    }

    private static int ColumnNameToIndex(string name)
    {
        int sum = 0;
        foreach (var ch in name.ToUpperInvariant())
        {
            if (ch < 'A' || ch > 'Z') continue;
            sum = sum * 26 + (ch - 'A' + 1);
        }
        return sum;
    }

    private static string EnsureUniqueSheetName(Sheets sheets, string desired)
    {
        var existing = sheets.Elements<Sheet>().Select(s => s.Name?.Value ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(desired)) return desired;
        int n = 2;
        while (existing.Contains($"{desired} ({n})")) n++;
        return $"{desired} ({n})";
    }

    private static string SanitizeFileName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "workbook" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
        return n.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? n[..^5] : n;
    }

    // -----------------------------
    // CSV helpers
    // -----------------------------

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        using var sr = new StringReader(csv ?? string.Empty);
        string? line;
        var sb = new StringBuilder();
        bool inQuotes = false;
        List<string> current = new();

        void pushField()
        {
            var f = sb.ToString();
            sb.Clear();
            // unescape double quotes
            if (f.Length > 0 && f.Contains('"')) f = f.Replace("\"\"", "\"");
            current.Add(f);
        }

        while ((line = sr.ReadLine()) != null)
        {
            int i = 0;
            while (i < line.Length)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        // lookahead for escaped quote
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2; // consume escaped quote
                            continue;
                        }
                        inQuotes = false; i++; continue;
                    }
                    sb.Append(ch); i++; continue;
                }
                else
                {
                    if (ch == ',') { pushField(); i++; continue; }
                    if (ch == '"') { inQuotes = true; i++; continue; }
                    sb.Append(ch); i++; continue;
                }
            }

            if (inQuotes)
            {
                // newline inside quoted field
                sb.Append('\n');
                continue;
            }

            pushField();
            rows.Add(current);
            current = new();
        }

        // last line if file doesn't end with newline
        if (inQuotes)
        {
            // unmatched quote: close it and push
            inQuotes = false; pushField(); rows.Add(current);
        }
        else if (current.Count > 0 && (current.Count > 1 || current[0].Length > 0))
        {
            rows.Add(current);
        }

        return rows;
    }

    private static string ToCsv(List<List<string>> table)
    {
        var sb = new StringBuilder();
        foreach (var row in table)
        {
            for (int i = 0; i < row.Count; i++)
            {
                var field = row[i] ?? string.Empty;
                bool needsQuote = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
                if (needsQuote)
                {
                    sb.Append('"');
                    sb.Append(field.Replace("\"", "\"\""));
                    sb.Append('"');
                }
                else sb.Append(field);

                if (i < row.Count - 1) sb.Append(',');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static List<List<string>> ReadSheetAsTable(WorkbookPart wb, WorksheetPart wsp)
    {
        var table = new List<List<string>>();
        var sst = wb.SharedStringTablePart?.SharedStringTable;
        var sheetData = wsp.Worksheet.GetFirstChild<SheetData>();
        if (sheetData == null) return table;

        // find max column across rows
        int maxCol = 0;
        foreach (var row in sheetData.Elements<Row>())
        {
            int cols = row.Elements<Cell>().Select(c => ColumnNameToIndex(new string(c.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray()))).DefaultIfEmpty(0).Max();
            if (cols > maxCol) maxCol = cols;
        }

        foreach (var row in sheetData.Elements<Row>())
        {
            var line = Enumerable.Repeat(string.Empty, maxCol).ToArray();
            foreach (var cell in row.Elements<Cell>())
            {
                var (col, _) = SplitAddress(cell.CellReference!);
                int idx = ColumnNameToIndex(col) - 1;
                string text = cell.CellValue?.Text ?? string.Empty;

                if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
                {
                    if (int.TryParse(text, out var sidx) && sst != null)
                    {
                        var ssi = sst.ElementAtOrDefault(sidx);
                        text = string.Concat(ssi?.InnerText);
                    }
                }

                line[idx] = text;
            }
            table.Add(line.ToList());
        }

        return table;
    }
}
