using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using MCPhappey.Tools.OpenXML.Excel;

namespace MCPhappey.Tests;

public class OpenXmlExcelTests
{
    [Fact]
    public void FormatOnlyRoundTripPreservesContentAndClonesSharedStyles()
    {
        using var stream = Fixture();
        string strings;
        string[] before;
        string originalStyle;
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            strings = wb.SharedStringTablePart!.SharedStringTable!.OuterXml;
            before = Cells(ws).Take(4).Select(Content).ToArray();
            originalStyle = wb.WorkbookStylesPart!.Stylesheet!.OuterXml;
            ExcelCellEditor.Edit(wb, ws, "$a$1:$d$1", "ignored", "unsupported", "formatOnly", new()
            {
                Font = new() { Bold = true, Italic = true, Strike = true, Underline = true, Name = "Arial", Size = 18, Color = "#cc0000" },
                Fill = new() { Color = "FFFF00" },
                Border = new() { Bottom = new() { Style = "thin", Color = "0000FF" } },
                Alignment = new() { Horizontal = "center", WrapText = true },
                NumberFormat = "0.000"
            });
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var wb = doc.WorkbookPart!;
            var cells = Cells(wb.WorksheetParts.Single()).ToArray();
            Assert.Equal(before, cells.Take(4).Select(Content).ToArray());
            Assert.Equal(strings, wb.SharedStringTablePart!.SharedStringTable!.OuterXml);
            Assert.Equal(1U, cells[4].StyleIndex!.Value);
            Assert.All(cells.Take(4), c => Assert.NotEqual(1U, c.StyleIndex!.Value));
            Assert.Single(cells.Take(4).Select(c => c.StyleIndex!.Value).Distinct());
            var styles = wb.WorkbookStylesPart!.Stylesheet!;
            Assert.Equal("Calibri", styles.Fonts!.Elements<Font>().First().FontName!.Val!.Value);
            Assert.Null(styles.Fonts.Elements<Font>().ElementAt(1).Bold);
            var patchedFormat = styles.CellFormats!.Elements<CellFormat>().ElementAt((int)cells[0].StyleIndex!.Value);
            var patchedFont = styles.Fonts.Elements<Font>().ElementAt((int)patchedFormat.FontId!.Value);
            Assert.Equal("Arial", patchedFont.FontName!.Val!.Value);
            Assert.Equal(18D, patchedFont.FontSize!.Val!.Value);
            Assert.True(patchedFont.Strike!.Val!.Value);
            Assert.Equal(UnderlineValues.Single, patchedFont.Underline!.Val!.Value);
            Assert.NotEqual(originalStyle, styles.OuterXml);
            AssertValid(doc);
        }
    }

    [Fact]
    public void ValueAndFormulaReplacementPreserveStyleAndRemoveOnlyStaleContent()
    {
        using var stream = Fixture();
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            ExcelCellEditor.Edit(wb, ws, "B1", "replacement", "string");
            ExcelCellEditor.Edit(wb, ws, "C1", "=SUM(D1,2)", "formula");
            ExcelCellEditor.Edit(wb, ws, "A1", "42.5", "number");
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var cells = Cells(doc.WorkbookPart!.WorksheetParts.Single()).ToArray();
            Assert.All(cells, c => Assert.Equal(1U, c.StyleIndex!.Value));
            Assert.Equal("42.5", cells[0].CellValue!.Text);
            Assert.Equal(CellValues.Number, cells[0].DataType!.Value);
            Assert.Null(cells[1].CellFormula);
            Assert.Equal(CellValues.SharedString, cells[1].DataType!.Value);
            Assert.Null(cells[2].InlineString);
            Assert.Null(cells[2].CellValue);
            Assert.Null(cells[2].DataType);
            Assert.Equal("SUM(D1,2)", cells[2].CellFormula!.Text);
            AssertValid(doc);
        }
    }

    [Fact]
    public void PatchHonorsRowColumnAndBaseXfInheritanceWithoutChangingNeighbors()
    {
        using var stream = Fixture();
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            var styles = wb.WorkbookStylesPart!.Stylesheet!;
            styles.CellStyleFormats!.Append(new CellFormat { FontId = 1, FillId = 0, BorderId = 0, NumberFormatId = 0 });
            styles.CellStyleFormats.Count = 2;
            styles.CellFormats!.Append(new CellFormat { FormatId = 1, ApplyFont = false });
            styles.CellFormats.Count = 3;
            ws.Worksheet.GetFirstChild<SheetData>()!.Append(new Row { RowIndex = 2, StyleIndex = 2, CustomFormat = true });
            ws.Worksheet.InsertBefore(new Columns(new Column { Min = 3, Max = 3, Style = 1, Width = 20, CustomWidth = true }), ws.Worksheet.GetFirstChild<SheetData>());
            ExcelCellEditor.Edit(wb, ws, "A2:B2", "", "string", "formatOnly", new() { Font = new() { Bold = true } });
            ExcelCellEditor.Edit(wb, ws, "C3", "", "string", "formatOnly", new() { Font = new() { Italic = true } });
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            var styles = wb.WorkbookStylesPart!.Stylesheet!;
            foreach (var address in new[] { "A2", "B2", "C3" })
            {
                var cell = Cells(ws).Single(c => c.CellReference!.Value == address);
                var xf = styles.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                var font = styles.Fonts!.Elements<Font>().ElementAt((int)xf.FontId!.Value);
                Assert.Equal("Consolas", font.FontName!.Val!.Value);
                Assert.Equal(14D, font.FontSize!.Val!.Value);
            }
            Assert.Equal(2U, ws.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().Single(r => r.RowIndex!.Value == 2).StyleIndex!.Value);
            Assert.Equal(1U, ws.Worksheet.GetFirstChild<Columns>()!.Elements<Column>().Single().Style!.Value);
            AssertValid(doc);
        }
    }

    [Fact]
    public void InspectionSeparatesCachedValuesFormulasAndFormattingResources()
    {
        using var stream = Fixture();
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wb = doc.WorkbookPart!;
        var payload = JsonSerializer.SerializeToElement(ExcelCellEditor.Inspect(wb, wb.WorksheetParts.Single(), "A1:F1"));
        var cells = payload.GetProperty("cells");
        Assert.Equal("shared text", cells[0].GetProperty("value").GetString());
        Assert.Equal("SUM(D1,2)", cells[1].GetProperty("formula").GetString());
        Assert.Equal("7", cells[1].GetProperty("value").GetString());
        Assert.Equal("inline text", cells[2].GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, cells[5].GetProperty("styleId").ValueKind);
        Assert.Equal(1U, cells[1].GetProperty("styleId").GetUInt32());
        Assert.Contains("Consolas", cells[0].GetProperty("resolved").GetProperty("font").GetString());
        Assert.True(payload.GetProperty("resources").TryGetProperty("styles", out _));
        Assert.True(payload.GetProperty("resources").TryGetProperty("theme", out _));
        Assert.True(payload.GetProperty("resources").TryGetProperty("merges", out _));
        Assert.True(payload.GetProperty("resources").TryGetProperty("conditionalFormatting", out _));
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    [InlineData("B2:A1")]
    [InlineData("A1:A10001")]
    [InlineData("Sheet1!A1")]
    [InlineData("A1:B2:C3")]
    [InlineData("A1 garbage")]
    public void InvalidOrOversizedAddressesAreRejected(string address)
        => Assert.Throws<ArgumentException>(() => ExcelCellEditor.ParseRange(address));

    [Fact]
    public void BoundaryAndAbsoluteAddressesAreAccepted()
    {
        Assert.Equal((16384, 1048576, 16384, 1048576), ExcelCellEditor.ParseRange("$XFD$1048576"));
        Assert.Equal((1, 1, 100, 100), ExcelCellEditor.ParseRange("A1:CV100"));
    }

    [Fact]
    public void FormatOnlyWithoutPatchIsNoOpAndFalseBooleanPatchIsExplicit()
    {
        using var stream = Fixture();
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            var before = ws.Worksheet.OuterXml;
            ExcelCellEditor.Edit(wb, ws, "Z100", "", "ignored", "formatOnly");
            Assert.Equal(before, ws.Worksheet.OuterXml);
            ExcelCellEditor.Edit(wb, ws, "A1", "", "ignored", "formatOnly", new() { Font = new() { Bold = true, Italic = true } });
            ExcelCellEditor.Edit(wb, ws, "A1", "", "ignored", "formatOnly", new() { Font = new() { Bold = false } });
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var wb = doc.WorkbookPart!;
            var cell = Cells(wb.WorksheetParts.Single()).First();
            var xf = wb.WorkbookStylesPart!.Stylesheet!.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
            var font = wb.WorkbookStylesPart.Stylesheet.Fonts!.Elements<Font>().ElementAt((int)xf.FontId!.Value);
            Assert.False(font.Bold!.Val!.Value);
            Assert.True(font.Italic!.Val!.Value);
            Assert.Equal("shared text", wb.SharedStringTablePart!.SharedStringTable!.InnerText);
            AssertValid(doc);
        }
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("array")]
    [InlineData("dataTable")]
    public void ComplexFormulaRegionsRejectReplacementButAllowFormatting(string formulaType)
    {
        using var stream = Fixture();
        string masterFormula;
        string? memberFormula;
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            var master = ExcelPlugin.InsertCellInWorksheet(ws, "A2");
            master.CellFormula = new CellFormula("SUM(D1,2)");
            master.CellFormula.SetAttribute(new OpenXmlAttribute("t", "", formulaType));
            master.CellFormula.SetAttribute(new OpenXmlAttribute("ref", "", "A2:B4"));
            master.CellValue = new CellValue("7");
            var member = ExcelPlugin.InsertCellInWorksheet(ws, "B2");
            member.CellValue = new CellValue("7");
            if (formulaType == "shared")
            {
                master.CellFormula.SetAttribute(new OpenXmlAttribute("si", "", "0"));
                member.CellFormula = new CellFormula();
                member.CellFormula.SetAttribute(new OpenXmlAttribute("t", "", "shared"));
                member.CellFormula.SetAttribute(new OpenXmlAttribute("si", "", "0"));
            }
            var implicitMember = ExcelPlugin.InsertCellInWorksheet(ws, "B4");
            implicitMember.CellValue = new CellValue("7");
            masterFormula = master.CellFormula.OuterXml;
            memberFormula = member.CellFormula?.OuterXml;
            var before = ws.Worksheet.OuterXml;
            var beforeStyles = wb.WorkbookStylesPart!.Stylesheet!.OuterXml;
            foreach (var address in new[] { "A2", "B2", "B4", "A1:A2" })
                Assert.Throws<ArgumentException>(() => ExcelCellEditor.Edit(wb, ws, address, "replacement", "string",
                    formatting: new() { Font = new() { Bold = true } }));
            Assert.Equal(before, ws.Worksheet.OuterXml);
            Assert.Equal(beforeStyles, wb.WorkbookStylesPart.Stylesheet.OuterXml);
            ExcelCellEditor.Edit(wb, ws, "A2:B4", "", "ignored", "formatOnly", new() { Font = new() { Bold = true } });
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var cells = Cells(doc.WorkbookPart!.WorksheetParts.Single()).ToDictionary(c => c.CellReference!.Value!);
            Assert.Equal(masterFormula, cells["A2"].CellFormula!.OuterXml);
            Assert.Equal(memberFormula, cells["B2"].CellFormula?.OuterXml);
            Assert.Null(cells["B4"].CellFormula);
            Assert.Equal("7", cells["A2"].CellValue!.Text);
            Assert.Equal("7", cells["B2"].CellValue!.Text);
            Assert.Equal("7", cells["B4"].CellValue!.Text);
            AssertValid(doc);
        }
    }

    [Fact]
    public void UnknownValueTypeRetainsLegacyStringFallback()
    {
        using var stream = Fixture();
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wb = doc.WorkbookPart!;
        var ws = wb.WorksheetParts.Single();
        ExcelCellEditor.Edit(wb, ws, "A1", "legacy fallback", "unknown");
        var cell = Cells(ws).First();
        Assert.Equal(CellValues.SharedString, cell.DataType!.Value);
        Assert.Equal("legacy fallback", wb.SharedStringTablePart!.SharedStringTable!.Elements<SharedStringItem>().Last().InnerText);
        Assert.Equal(1U, cell.StyleIndex!.Value);
    }

    [Fact]
    public void LargeComplexFormulaReferenceDoesNotUseRequestCellLimit()
    {
        using var stream = Fixture();
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wb = doc.WorkbookPart!;
        var ws = wb.WorksheetParts.Single();
        var master = Cells(ws).First();
        master.DataType = null;
        master.CellFormula = new CellFormula("1");
        master.CellFormula.SetAttribute(new OpenXmlAttribute("t", "", "array"));
        master.CellFormula.SetAttribute(new OpenXmlAttribute("ref", "", "A1:XFD1048576"));
        var error = Assert.Throws<ArgumentException>(() =>
            ExcelCellEditor.Edit(wb, ws, "XFD1048576", "replacement", "string"));
        Assert.Contains("array formula region", error.Message);
    }

    [Fact]
    public void CalculationChainTargetsRejectContentChangesButFormattingPreservesChainAndCalculationProperties()
    {
        using var stream = Fixture();
        string chainXml;
        string calculationXml;
        string formulaXml;
        using (var doc = SpreadsheetDocument.Open(stream, true))
        {
            var wb = doc.WorkbookPart!;
            var ws = wb.WorksheetParts.Single();
            var first = Cells(ws).First();
            first.DataType = null;
            first.CellFormula = new CellFormula("D1");
            first.CellValue = new CellValue("5");
            var chain = wb.AddNewPart<CalculationChainPart>();
            chain.CalculationChain = new CalculationChain();
            foreach (var address in new[] { "A1", "B1" })
            {
                var entry = new CalculationCell();
                entry.SetAttribute(new OpenXmlAttribute("r", "", address));
                if (address == "A1") entry.SetAttribute(new OpenXmlAttribute("i", "", "1"));
                chain.CalculationChain.Append(entry);
            }
            var calculation = new CalculationProperties();
            calculation.SetAttribute(new OpenXmlAttribute("calcId", "", "123"));
            calculation.SetAttribute(new OpenXmlAttribute("fullCalcOnLoad", "", "0"));
            wb.Workbook.Append(calculation);
            chainXml = chain.CalculationChain.OuterXml;
            calculationXml = calculation.OuterXml;
            formulaXml = first.CellFormula.OuterXml;
            var before = ws.Worksheet.OuterXml;
            foreach (var address in new[] { "A1", "B1", "A1:D1" })
            {
                var error = Assert.Throws<ArgumentException>(() => ExcelCellEditor.Edit(wb, ws, address, "5", "number",
                    formatting: new() { Font = new() { Italic = true } }));
                Assert.Contains("calculation chain", error.Message);
                Assert.Equal(before, ws.Worksheet.OuterXml);
            }
            ExcelCellEditor.Edit(wb, ws, "A1:B1", "", "ignored", "formatOnly", new() { Font = new() { Bold = true } });
        }
        using (var doc = SpreadsheetDocument.Open(stream, false))
        {
            var wb = doc.WorkbookPart!;
            Assert.Equal(chainXml, wb.CalculationChainPart!.CalculationChain!.OuterXml);
            Assert.Equal(calculationXml, wb.Workbook.GetFirstChild<CalculationProperties>()!.OuterXml);
            Assert.Equal(formulaXml, Cells(wb.WorksheetParts.Single()).First().CellFormula!.OuterXml);
            AssertValid(doc);
        }
    }

    [Fact]
    public void CalculationChainSameAddressOnAnotherSheetDoesNotBlockReplacement()
    {
        using var stream = Fixture();
        using var doc = SpreadsheetDocument.Open(stream, true);
        var wb = doc.WorkbookPart!;
        var ws = wb.WorksheetParts.Single();
        var other = wb.AddNewPart<WorksheetPart>();
        other.Worksheet = new Worksheet(new SheetData(new Row(
            new Cell { CellReference = "B1", CellFormula = new CellFormula("1"), CellValue = new CellValue("1") }) { RowIndex = 1 }));
        wb.Workbook.Sheets!.Append(new Sheet { Id = wb.GetIdOfPart(other), SheetId = 2, Name = "Other" });
        var chain = wb.AddNewPart<CalculationChainPart>();
        var entry = new CalculationCell();
        entry.SetAttribute(new OpenXmlAttribute("r", "", "B1"));
        entry.SetAttribute(new OpenXmlAttribute("i", "", "2"));
        chain.CalculationChain = new CalculationChain(entry);
        var chainXml = chain.CalculationChain.OuterXml;
        ExcelCellEditor.Edit(wb, ws, "B1", "replacement", "string");
        Assert.Null(Cells(ws).Single(c => c.CellReference!.Value == "B1").CellFormula);
        Assert.Equal(chainXml, chain.CalculationChain.OuterXml);
    }

    [Fact]
    public void EditingUnstyledWorkbookUsesExpandableStreamAndProducesValidStyles()
    {
        byte[] bytes;
        using (var fixture = new MemoryStream())
        {
            using (var doc = SpreadsheetDocument.Create(fixture, SpreadsheetDocumentType.Workbook))
            {
                var wb = doc.AddWorkbookPart();
                wb.Workbook = new Workbook();
                var ws = wb.AddNewPart<WorksheetPart>();
                ws.Worksheet = new Worksheet(new SheetData());
                wb.Workbook.Append(new Sheets(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Sheet1" }));
            }
            bytes = fixture.ToArray();
        }
        using var stream = ExcelCellEditor.OpenEditableStream(bytes);
        using (var doc = SpreadsheetDocument.Open(stream, true))
            ExcelCellEditor.Edit(doc.WorkbookPart!, doc.WorkbookPart!.WorksheetParts.Single(), "A1:D20", "1", "number",
                formatting: new() { Font = new() { Bold = true }, Fill = new() { Color = "00FF00" } });
        Assert.True(stream.Length > bytes.Length);
        using (var doc = SpreadsheetDocument.Open(stream, false))
            AssertValid(doc);
    }

    private static IEnumerable<Cell> Cells(WorksheetPart ws) => ws.Worksheet.Descendants<Cell>();
    private static void AssertValid(SpreadsheetDocument doc)
    {
        var errors = new OpenXmlValidator().Validate(doc).Select(e => $"{e.Description} {e.Path?.XPath}").ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors));
    }
    private static string Content(Cell cell) => $"{cell.DataType?.InnerText}|{cell.CellValue?.OuterXml}|{cell.CellFormula?.OuterXml}|{cell.InlineString?.OuterXml}";

    private static MemoryStream Fixture()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var ws = wb.AddNewPart<WorksheetPart>();
            ws.Worksheet = new Worksheet(new SheetData(new Row(
                new Cell { CellReference = "A1", StyleIndex = 1, DataType = CellValues.SharedString, CellValue = new CellValue("0") },
                new Cell { CellReference = "B1", StyleIndex = 1, CellFormula = new CellFormula("SUM(D1,2)"), CellValue = new CellValue("7") },
                new Cell { CellReference = "C1", StyleIndex = 1, DataType = CellValues.InlineString, InlineString = new InlineString(new Text("inline text")) },
                new Cell { CellReference = "D1", StyleIndex = 1, DataType = CellValues.Number, CellValue = new CellValue("5") },
                new Cell { CellReference = "E1", StyleIndex = 1, DataType = CellValues.Number, CellValue = new CellValue("9") }) { RowIndex = 1 }));
            wb.Workbook.Append(new Sheets(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Sheet1" }));
            wb.AddNewPart<SharedStringTablePart>().SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("shared text")));
            wb.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(
                new Fonts(new Font(new FontName { Val = "Calibri" }), new Font(new FontSize { Val = 14 }, new FontName { Val = "Consolas" })) { Count = 2 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 0 }) { Count = 1 },
                new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 0, FormatId = 0 },
                    new CellFormat { FontId = 1, FillId = 0, BorderId = 0, NumberFormatId = 0, FormatId = 0 }) { Count = 2 });
        }
        stream.Position = 0;
        return stream;
    }
}
