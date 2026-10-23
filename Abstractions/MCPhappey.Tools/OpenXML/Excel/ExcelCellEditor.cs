using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace MCPhappey.Tools.OpenXML.Excel;

public sealed class ExcelFormattingPatch
{
    public ExcelFontPatch? Font { get; set; }
    public ExcelFillPatch? Fill { get; set; }
    public ExcelBorderPatch? Border { get; set; }
    public ExcelAlignmentPatch? Alignment { get; set; }
    public string? NumberFormat { get; set; }
}

public sealed class ExcelFontPatch
{
    public string? Name { get; set; }
    public double? Size { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Strike { get; set; }
    public bool? Underline { get; set; }
    public string? Color { get; set; }
}

public sealed class ExcelFillPatch
{
    public string? Color { get; set; }
    public string? BackgroundColor { get; set; }
    public string? Pattern { get; set; }
}

public sealed class ExcelBorderPatch
{
    public ExcelBorderSidePatch? Left { get; set; }
    public ExcelBorderSidePatch? Right { get; set; }
    public ExcelBorderSidePatch? Top { get; set; }
    public ExcelBorderSidePatch? Bottom { get; set; }
    public ExcelBorderSidePatch? Diagonal { get; set; }
    public bool? DiagonalUp { get; set; }
    public bool? DiagonalDown { get; set; }
}

public sealed class ExcelBorderSidePatch
{
    public string? Style { get; set; }
    public string? Color { get; set; }
}

public sealed class ExcelAlignmentPatch
{
    public string? Horizontal { get; set; }
    public string? Vertical { get; set; }
    public bool? WrapText { get; set; }
    public bool? ShrinkToFit { get; set; }
    public uint? TextRotation { get; set; }
    public uint? Indent { get; set; }
}

internal static class ExcelCellEditor
{
    internal const int MaxCells = 10000;

    internal static MemoryStream OpenEditableStream(byte[] bytes)
    {
        var stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        return stream;
    }

    internal static (int Left, int Top, int Right, int Bottom) ParseRange(string address, bool enforceCellLimit = true)
    {
        var parts = (address ?? "").Split(':');
        if (parts.Length is < 1 or > 2) throw new ArgumentException("Expected an A1 cell or rectangular range.");
        static (int Column, int Row) Parse(string text)
        {
            var match = Regex.Match(text, @"^\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})$");
            if (!match.Success) throw new ArgumentException($"Invalid A1 address '{text}'.");
            int column = 0;
            foreach (char c in match.Groups[1].Value.ToUpperInvariant()) column = column * 26 + c - 'A' + 1;
            int row = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (column > 16384 || row > 1048576) throw new ArgumentException("Address exceeds Excel worksheet bounds.");
            return (column, row);
        }
        var first = Parse(parts[0]);
        var last = Parse(parts[^1]);
        if (last.Column < first.Column || last.Row < first.Row) throw new ArgumentException("Range must run from top-left to bottom-right.");
        if (enforceCellLimit && (long)(last.Column - first.Column + 1) * (last.Row - first.Row + 1) > MaxCells)
            throw new ArgumentException($"A request may select at most {MaxCells} cells.");
        return (first.Column, first.Row, last.Column, last.Row);
    }

    private static IEnumerable<string> Addresses(string address)
    {
        var range = ParseRange(address);
        for (int row = range.Top; row <= range.Bottom; row++)
            for (int col = range.Left; col <= range.Right; col++)
            {
                string name = "";
                for (int c = col; c > 0; c = (c - 1) / 26) name = (char)('A' + (c - 1) % 26) + name;
                yield return name + row.ToString(CultureInfo.InvariantCulture);
            }
    }

    internal static void Edit(WorkbookPart wb, WorksheetPart ws, string address, string value, string type,
        string operation = "setValue", ExcelFormattingPatch? formatting = null)
    {
        ParseRange(address);
        bool formatOnly = string.Equals(operation, "formatOnly", StringComparison.OrdinalIgnoreCase);
        if (!formatOnly && !string.Equals(operation, "setValue", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("operation must be setValue or formatOnly.");
        if (formatOnly && formatting == null) return;
        type = (type ?? "string").Trim().ToLowerInvariant();
        if (!formatOnly && type == "number" && (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)))
            throw new ArgumentException("Expected a finite invariant-culture number.");
        if (!formatOnly)
        {
            RejectComplexFormulaReplacement(ws, address);
            RejectCalculationChainReplacement(wb, ws, address);
        }
        var styleMap = new Dictionary<uint, uint>();
        foreach (var a1 in Addresses(address))
        {
            var cell = ExcelPlugin.InsertCellInWorksheet(ws, a1);
            if (formatting != null)
            {
                uint source = EffectiveStyle(ws, cell, a1).Id;
                if (!styleMap.TryGetValue(source, out uint target))
                    styleMap[source] = target = PatchStyle(wb, source, formatting);
                cell.StyleIndex = target;
            }
            if (formatOnly) continue;
            cell.CellFormula = null;
            cell.InlineString = null;
            cell.CellValue = null;
            cell.DataType = null;
            switch (type)
            {
                case "formula":
                    cell.CellFormula = new CellFormula(value.StartsWith('=') ? value[1..] : value);
                    break;
                case "number":
                    cell.CellValue = new CellValue(value);
                    cell.DataType = CellValues.Number;
                    break;
                case "bool":
                case "boolean":
                    cell.CellValue = new CellValue(value.Trim().ToLowerInvariant() is "true" or "1" or "yes" ? "1" : "0");
                    cell.DataType = CellValues.Boolean;
                    break;
                default:
                    ExcelPlugin.SetSharedString(wb, cell, value);
                    break;
            }
        }
        ws.Worksheet.Save();
    }

    private static void RejectComplexFormulaReplacement(WorksheetPart ws, string address)
    {
        var selected = ParseRange(address);
        bool Intersects((int Left, int Top, int Right, int Bottom) region) =>
            selected.Left <= region.Right && selected.Right >= region.Left
            && selected.Top <= region.Bottom && selected.Bottom >= region.Top;
        foreach (var cell in ws.Worksheet.Descendants<Cell>())
        {
            var formula = cell.CellFormula;
            if (formula == null) continue;
            var formulaType = formula.GetAttributes().FirstOrDefault(a => a.LocalName == "t").Value;
            if (formulaType is not ("shared" or "array" or "dataTable")) continue;
            var reference = formula.GetAttributes().FirstOrDefault(a => a.LocalName == "ref").Value;
            if ((!string.IsNullOrEmpty(cell.CellReference?.Value) && Intersects(ParseRange(cell.CellReference.Value, false)))
                || (!string.IsNullOrEmpty(reference) && Intersects(ParseRange(reference, false))))
                throw new ArgumentException($"Content replacement intersects a {formulaType} formula region. Only formatOnly is supported for these cells.");
        }
    }

    private static void RejectCalculationChainReplacement(WorkbookPart wb, WorksheetPart ws, string address)
    {
        var chain = wb.CalculationChainPart?.CalculationChain;
        if (chain == null) return;
        var selected = ParseRange(address);
        var targetSheetId = wb.Workbook.Sheets?.Elements<Sheet>()
            .FirstOrDefault(s => s.Id?.Value == wb.GetIdOfPart(ws))?.SheetId?.Value;
        uint? chainSheetId = null;
        foreach (var entry in chain.Elements<CalculationCell>())
        {
            var explicitSheet = entry.GetAttributes().FirstOrDefault(a => a.LocalName == "i").Value;
            if (!string.IsNullOrEmpty(explicitSheet))
                chainSheetId = uint.TryParse(explicitSheet, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            if (chainSheetId != null && targetSheetId != null && chainSheetId != targetSheetId) continue;
            var reference = entry.GetAttributes().FirstOrDefault(a => a.LocalName == "r").Value;
            if (string.IsNullOrEmpty(reference)) throw new InvalidDataException("Calculation chain entry is missing its cell reference.");
            var region = ParseRange(reference, false);
            if (selected.Left <= region.Right && selected.Right >= region.Left
                && selected.Top <= region.Bottom && selected.Bottom >= region.Top)
                throw new ArgumentException("Content replacement targets a cell referenced by the calculation chain. Only formatOnly is supported for these cells.");
        }
    }

    private static (uint Id, string Source) EffectiveStyle(WorksheetPart ws, Cell? cell, string address)
    {
        if (cell?.StyleIndex != null) return (cell.StyleIndex.Value, "cell");
        var range = ParseRange(address);
        var row = ws.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == range.Top);
        if (row?.StyleIndex != null && row.CustomFormat?.Value == true) return (row.StyleIndex.Value, "row");
        var column = ws.Worksheet.Elements<Columns>().SelectMany(c => c.Elements<Column>())
            .LastOrDefault(c => c.Min?.Value <= range.Left && c.Max?.Value >= range.Left);
        return column?.Style != null ? (column.Style.Value, "column") : (0, "default");
    }

    private static Stylesheet Styles(WorkbookPart wb)
    {
        var part = wb.WorkbookStylesPart ?? wb.AddNewPart<WorkbookStylesPart>();
        if (part.Stylesheet == null)
            part.Stylesheet = new Stylesheet(
                new Fonts(new Font()) { Count = 1 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 0, FormatId = 0 }) { Count = 1 });
        return part.Stylesheet;
    }

    private static T? At<T>(OpenXmlCompositeElement? list, uint id) where T : OpenXmlElement
        => list?.Elements<T>().ElementAtOrDefault(checked((int)id));

    private static CellFormat Resolve(Stylesheet? styles, uint id)
    {
        var direct = At<CellFormat>(styles?.CellFormats, id);
        var basis = At<CellFormat>(styles?.CellStyleFormats, direct?.FormatId?.Value ?? 0);
        var result = (CellFormat?)direct?.CloneNode(true) ?? new CellFormat();
        if (direct?.FontId == null || direct.ApplyFont?.Value == false) result.FontId = basis?.FontId?.Value ?? 0;
        if (direct?.FillId == null || direct.ApplyFill?.Value == false) result.FillId = basis?.FillId?.Value ?? 0;
        if (direct?.BorderId == null || direct.ApplyBorder?.Value == false) result.BorderId = basis?.BorderId?.Value ?? 0;
        if (direct?.NumberFormatId == null || direct.ApplyNumberFormat?.Value == false) result.NumberFormatId = basis?.NumberFormatId?.Value ?? 0;
        if (direct?.Alignment == null || direct.ApplyAlignment?.Value == false)
            result.Alignment = (Alignment?)basis?.Alignment?.CloneNode(true);
        return result;
    }

    private static void Replace<T>(OpenXmlCompositeElement parent, T child) where T : OpenXmlElement
    {
        parent.RemoveAllChildren<T>();
        parent.AddChild(child, true);
    }

    private static string Rgb(string color)
    {
        color = color.TrimStart('#').ToUpperInvariant();
        if (!Regex.IsMatch(color, @"^([0-9A-F]{6}|[0-9A-F]{8})$")) throw new ArgumentException("Colors must be RRGGBB or AARRGGBB hex.");
        return color.Length == 6 ? "FF" + color : color;
    }

    private static uint AppendStyle<T>(OpenXmlCompositeElement list, T item) where T : OpenXmlElement
    {
        var items = list.Elements<T>().ToList();
        int existing = items.FindIndex(x => x.OuterXml == item.OuterXml);
        if (existing >= 0) return (uint)existing;
        list.Append(item);
        list.SetAttribute(new OpenXmlAttribute("count", "", (items.Count + 1).ToString(CultureInfo.InvariantCulture)));
        return (uint)items.Count;
    }

    private static void Attribute(OpenXmlElement element, string name, string? value)
    {
        if (value != null) element.SetAttribute(new OpenXmlAttribute(name, "", value));
    }

    private static void Choice(OpenXmlElement element, string name, string? value, string allowed)
    {
        if (value == null) return;
        if (!allowed.Split(' ').Contains(value)) throw new ArgumentException($"Unsupported {name}: {value}.");
        Attribute(element, name, value);
    }

    private static uint PatchStyle(WorkbookPart wb, uint source, ExcelFormattingPatch patch)
    {
        var styles = Styles(wb);
        var format = (CellFormat?)At<CellFormat>(styles.CellFormats, source)?.CloneNode(true) ?? new CellFormat();
        var resolved = Resolve(styles, source);
        if (patch.Font is { } fp)
        {
            var font = (Font?)At<Font>(styles.Fonts, resolved.FontId?.Value ?? 0)?.CloneNode(true) ?? new Font();
            if (fp.Name != null) Replace(font, new FontName { Val = fp.Name });
            if (fp.Size != null)
            {
                if (fp.Size <= 0 || fp.Size > 409 || !double.IsFinite(fp.Size.Value)) throw new ArgumentException("Font size must be in (0, 409].");
                Replace(font, new FontSize { Val = fp.Size.Value });
            }
            if (fp.Bold != null) Replace(font, new Bold { Val = fp.Bold.Value });
            if (fp.Italic != null) Replace(font, new Italic { Val = fp.Italic.Value });
            if (fp.Strike != null) Replace(font, new Strike { Val = fp.Strike.Value });
            if (fp.Underline != null) Replace(font, new Underline { Val = fp.Underline.Value ? UnderlineValues.Single : UnderlineValues.None });
            if (fp.Color != null) Replace(font, new Color { Rgb = Rgb(fp.Color) });
            styles.Fonts ??= new Fonts();
            format.FontId = AppendStyle(styles.Fonts, font);
            format.ApplyFont = true;
        }
        if (patch.Fill is { } fillPatch)
        {
            var fill = (Fill?)At<Fill>(styles.Fills, resolved.FillId?.Value ?? 0)?.CloneNode(true) ?? new Fill();
            var pattern = (PatternFill?)fill.PatternFill?.CloneNode(true) ?? new PatternFill { PatternType = PatternValues.Solid };
            Choice(pattern, "patternType", fillPatch.Pattern, "none solid mediumGray darkGray lightGray darkHorizontal darkVertical darkDown darkUp darkGrid darkTrellis lightHorizontal lightVertical lightDown lightUp lightGrid lightTrellis gray125 gray0625");
            if (fillPatch.Color != null) pattern.ForegroundColor = new ForegroundColor { Rgb = Rgb(fillPatch.Color) };
            if (fillPatch.BackgroundColor != null) pattern.BackgroundColor = new BackgroundColor { Rgb = Rgb(fillPatch.BackgroundColor) };
            if (fillPatch.Color != null && fillPatch.Pattern == null) pattern.PatternType = PatternValues.Solid;
            fill.RemoveAllChildren();
            fill.Append(pattern);
            styles.Fills ??= new Fills();
            format.FillId = AppendStyle(styles.Fills, fill);
            format.ApplyFill = true;
        }
        if (patch.Border is { } bp)
        {
            var border = (Border?)At<Border>(styles.Borders, resolved.BorderId?.Value ?? 0)?.CloneNode(true) ?? new Border();
            void Side<T>(ExcelBorderSidePatch? side) where T : BorderPropertiesType, new()
            {
                if (side == null) return;
                var element = (T?)border.GetFirstChild<T>()?.CloneNode(true) ?? new T();
                Choice(element, "style", side.Style, "none thin medium dashed dotted thick double hair mediumDashed dashDot mediumDashDot dashDotDot mediumDashDotDot slantDashDot");
                if (side.Color != null) element.Color = new Color { Rgb = Rgb(side.Color) };
                Replace(border, element);
            }
            Side<LeftBorder>(bp.Left); Side<RightBorder>(bp.Right); Side<TopBorder>(bp.Top);
            Side<BottomBorder>(bp.Bottom); Side<DiagonalBorder>(bp.Diagonal);
            if (bp.DiagonalUp != null) border.DiagonalUp = bp.DiagonalUp.Value;
            if (bp.DiagonalDown != null) border.DiagonalDown = bp.DiagonalDown.Value;
            styles.Borders ??= new Borders();
            format.BorderId = AppendStyle(styles.Borders, border);
            format.ApplyBorder = true;
        }
        if (patch.Alignment is { } ap)
        {
            var alignment = (Alignment?)resolved.Alignment?.CloneNode(true) ?? new Alignment();
            Choice(alignment, "horizontal", ap.Horizontal, "general left center right fill justify centerContinuous distributed");
            Choice(alignment, "vertical", ap.Vertical, "top center bottom justify distributed");
            if (ap.WrapText != null) alignment.WrapText = ap.WrapText.Value;
            if (ap.ShrinkToFit != null) alignment.ShrinkToFit = ap.ShrinkToFit.Value;
            if (ap.TextRotation != null)
            {
                if (ap.TextRotation > 180 && ap.TextRotation != 255) throw new ArgumentException("Text rotation must be 0-180 or 255.");
                alignment.TextRotation = ap.TextRotation.Value;
            }
            if (ap.Indent != null)
            {
                if (ap.Indent > 250) throw new ArgumentException("Indent must be 0-250.");
                alignment.Indent = ap.Indent.Value;
            }
            format.Alignment = alignment;
            format.ApplyAlignment = true;
        }
        if (patch.NumberFormat != null)
        {
            styles.NumberingFormats ??= new NumberingFormats();
            var number = styles.NumberingFormats.Elements<NumberingFormat>().FirstOrDefault(n => n.FormatCode?.Value == patch.NumberFormat);
            if (number == null)
            {
                uint id = Math.Max(163U, styles.NumberingFormats.Elements<NumberingFormat>().Max(n => (uint?)n.NumberFormatId?.Value) ?? 163U) + 1;
                number = new NumberingFormat { NumberFormatId = id, FormatCode = patch.NumberFormat };
                styles.NumberingFormats.Append(number);
                styles.NumberingFormats.Count = (uint)styles.NumberingFormats.ChildElements.Count;
            }
            format.NumberFormatId = number.NumberFormatId!.Value;
            format.ApplyNumberFormat = true;
        }
        styles.CellFormats ??= new CellFormats();
        uint result = AppendStyle(styles.CellFormats, format);
        styles.Save();
        return result;
    }

    internal static object Inspect(WorkbookPart wb, WorksheetPart ws, string address)
    {
        var range = ParseRange(address);
        var addresses = Addresses(address).ToArray();
        var selected = addresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cells = ws.Worksheet.GetFirstChild<SheetData>()?.Descendants<Cell>()
            .Where(c => c.CellReference != null && selected.Contains(c.CellReference.Value!))
            .ToDictionary(c => c.CellReference!.Value!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, Cell>(StringComparer.OrdinalIgnoreCase);
        var styles = wb.WorkbookStylesPart?.Stylesheet;
        object Components(CellFormat? format) => new
        {
            fontId = format?.FontId?.Value ?? 0, fillId = format?.FillId?.Value ?? 0,
            borderId = format?.BorderId?.Value ?? 0, numberFormatId = format?.NumberFormatId?.Value ?? 0,
            font = At<Font>(styles?.Fonts, format?.FontId?.Value ?? 0)?.OuterXml,
            fill = At<Fill>(styles?.Fills, format?.FillId?.Value ?? 0)?.OuterXml,
            border = At<Border>(styles?.Borders, format?.BorderId?.Value ?? 0)?.OuterXml,
            alignment = format?.Alignment?.OuterXml,
            numberFormat = styles?.NumberingFormats?.Elements<NumberingFormat>().FirstOrDefault(n => n.NumberFormatId?.Value == (format?.NumberFormatId?.Value ?? 0))?.FormatCode?.Value
                ?? BuiltInNumberFormat(format?.NumberFormatId?.Value ?? 0)
        };
        return new
        {
            address,
            cells = addresses.Select(a1 =>
            {
                cells.TryGetValue(a1, out var cell);
                var effective = EffectiveStyle(ws, cell, a1);
                var direct = At<CellFormat>(styles?.CellFormats, cell?.StyleIndex?.Value ?? 0);
                var rawValue = cell?.CellValue?.Text;
                string? value = cell?.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText : rawValue;
                if (cell?.DataType?.Value == CellValues.SharedString && int.TryParse(rawValue, out int index) && index >= 0)
                    value = wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText;
                return new
                {
                    address = a1, value, rawValue, formula = cell?.CellFormula?.Text,
                    formulaXml = cell?.CellFormula?.OuterXml, dataType = cell?.DataType?.InnerText,
                    styleId = cell?.StyleIndex?.Value, effectiveStyleId = effective.Id, styleSource = effective.Source,
                    raw = cell?.OuterXml, direct = cell?.StyleIndex == null ? null : Components(direct),
                    directStyleXml = cell?.StyleIndex == null ? null : direct?.OuterXml,
                    resolved = Components(Resolve(styles, effective.Id))
                };
            }).ToArray(),
            resources = new
            {
                styles = styles?.OuterXml, theme = wb.ThemePart?.Theme?.OuterXml,
                sheetDimension = ws.Worksheet.SheetDimension?.OuterXml,
                sheetFormat = ws.Worksheet.SheetFormatProperties?.OuterXml,
                columns = ws.Worksheet.Elements<Columns>().Select(c => c.OuterXml).ToArray(),
                rows = ws.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>()
                    .Where(r => r.RowIndex?.Value >= range.Top && r.RowIndex?.Value <= range.Bottom)
                    .Select(r => new { index = r.RowIndex?.Value, height = r.Height?.Value, styleId = r.StyleIndex?.Value, customFormat = r.CustomFormat?.Value }).ToArray(),
                merges = ws.Worksheet.Elements<MergeCells>().Select(m => m.OuterXml).ToArray(),
                conditionalFormatting = ws.Worksheet.Elements<ConditionalFormatting>().Select(c => c.OuterXml).ToArray()
            },
            resolution = "Resolved components include cell/row/column and base-XF inheritance. Theme/indexed colors remain symbolic; theme and style XML are supplied. Conditional formatting is reported, not evaluated. Formula values are cached, not recalculated."
        };
    }

    private static string? BuiltInNumberFormat(uint id) => id switch
    {
        0 => "General", 1 => "0", 2 => "0.00", 3 => "#,##0", 4 => "#,##0.00",
        9 => "0%", 10 => "0.00%", 11 => "0.00E+00", 12 => "# ?/?", 13 => "# ??/??",
        14 => "mm-dd-yy", 15 => "d-mmm-yy", 16 => "d-mmm", 17 => "mmm-yy",
        18 => "h:mm AM/PM", 19 => "h:mm:ss AM/PM", 20 => "h:mm", 21 => "h:mm:ss", 22 => "m/d/yy h:mm",
        37 => "#,##0 ;(#,##0)", 38 => "#,##0 ;[Red](#,##0)", 39 => "#,##0.00;(#,##0.00)",
        40 => "#,##0.00;[Red](#,##0.00)", 45 => "mm:ss", 46 => "[h]:mm:ss", 47 => "mmss.0",
        48 => "##0.0E+0", 49 => "@", _ => null
    };
}
