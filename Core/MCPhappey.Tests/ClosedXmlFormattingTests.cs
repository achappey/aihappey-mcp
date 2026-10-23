using System.Text.Json;
using ClosedXML.Excel;
using MCPhappey.Tools.ClosedXML;

namespace MCPhappey.Tests;

public sealed class ClosedXmlFormattingTests
{
    [Fact]
    public void Formatting_only_roundtrips_preserving_formulas_values_and_neighbor_styles()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell("A1").Value = 3;
        sheet.Cell("A2").FormulaA1 = "A1*2";
        sheet.Cell("A1").Style.Font.Italic = true;
        sheet.Cell("A2").Style.Font.FontName = "Arial";
        sheet.Cell("A2").Style.NumberFormat.Format = "0.000";
        sheet.Cell("B1").Value = "neighbor";
        sheet.Cell("B1").Style.Fill.BackgroundColor = XLColor.Blue;
        var neighborStyle = sheet.Cell("B1").Style;

        ClosedXMLPlugin.EditCells(sheet, "A1", "ignored", "formatOnly", "A2",
            new ClosedXmlFormattingPatch
            {
                Font = new() { Bold = true, Color = "#FF0000" },
                Fill = new() { BackgroundColor = "#FFFF00" },
                Border = new() { Bottom = XLBorderStyleValues.Thin },
                Alignment = new() { WrapText = true }
            }, formula: "99");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        using var reopened = new XLWorkbook(stream);
        var actual = reopened.Worksheet("Sheet1");
        Assert.Equal(3d, actual.Cell("A1").GetDouble());
        Assert.Equal("A1*2", actual.Cell("A2").FormulaA1);
        Assert.Equal(6d, actual.Cell("A2").GetDouble());
        Assert.True(actual.Cell("A1").Style.Font.Italic);
        Assert.Equal("Arial", actual.Cell("A2").Style.Font.FontName);
        Assert.Equal("0.000", actual.Cell("A2").Style.NumberFormat.Format);
        foreach (var cell in actual.Range("A1:A2").Cells())
        {
            Assert.True(cell.Style.Font.Bold);
            Assert.Equal(XLColor.Red, cell.Style.Font.FontColor);
            Assert.Equal(XLColor.Yellow, cell.Style.Fill.BackgroundColor);
            Assert.Equal(XLBorderStyleValues.Thin, cell.Style.Border.BottomBorder);
            Assert.True(cell.Style.Alignment.WrapText);
        }
        Assert.Equal("neighbor", actual.Cell("B1").GetString());
        Assert.Equal(neighborStyle, actual.Cell("B1").Style);
    }

    [Fact]
    public void Value_and_explicit_formula_replacement_preserve_existing_style()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        var cell = sheet.Cell("A1");
        cell.FormulaA1 = "1+1";
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.Green;
        cell.Style.NumberFormat.Format = "0.00";
        var style = cell.Style;

        ClosedXMLPlugin.EditCells(sheet, "A1", "=literal");
        Assert.False(cell.HasFormula);
        Assert.Equal("=literal", cell.GetString());
        Assert.Equal(style, cell.Style);

        ClosedXMLPlugin.EditCells(sheet, "A1", "two\nlines");
        Assert.False(cell.Style.Alignment.WrapText);
        Assert.Equal(style, cell.Style);

        ClosedXMLPlugin.EditCells(sheet, "A1", "ignored", "formula", formula: "2+3");
        Assert.Equal("2+3", cell.FormulaA1);
        Assert.Equal(5d, cell.GetDouble());
        Assert.Equal(style, cell.Style);
    }

    [Fact]
    public void Patch_can_clear_flags_and_set_native_style_enums_without_replacing_other_properties()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        var cell = sheet.Cell("A1");
        cell.Value = "unchanged";
        cell.Style.Font.Bold = true;
        cell.Style.Font.Italic = true;
        cell.Style.Alignment.WrapText = true;
        cell.Style.Border.TopBorder = XLBorderStyleValues.Double;

        ClosedXMLPlugin.EditCells(sheet, "A1", "", "formatOnly", formatting: new()
        {
            Font = new() { Bold = false, Underline = XLFontUnderlineValues.Single },
            Alignment = new() { WrapText = false, Horizontal = XLAlignmentHorizontalValues.Center },
            Border = new() { Bottom = XLBorderStyleValues.Dashed },
            NumberFormatId = 2,
            NumberFormat = "0.0000"
        });

        Assert.Equal("unchanged", cell.GetString());
        Assert.False(cell.Style.Font.Bold);
        Assert.True(cell.Style.Font.Italic);
        Assert.Equal(XLFontUnderlineValues.Single, cell.Style.Font.Underline);
        Assert.False(cell.Style.Alignment.WrapText);
        Assert.Equal(XLAlignmentHorizontalValues.Center, cell.Style.Alignment.Horizontal);
        Assert.Equal(XLBorderStyleValues.Double, cell.Style.Border.TopBorder);
        Assert.Equal(XLBorderStyleValues.Dashed, cell.Style.Border.BottomBorder);
        Assert.Equal("0.0000", cell.Style.NumberFormat.Format);
    }

    [Fact]
    public void Inspection_keeps_legacy_value_and_reports_range_formatting_dimensions_merges_and_rules()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell("A1").FormulaA1 = "1+2";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("B1").Value = "text";
        sheet.Row(1).Height = 30;
        sheet.Column(2).Width = 20;
        sheet.Range("A2:B2").Merge();
        sheet.Range("A1:B1").AddConditionalFormat().WhenGreaterThan(1).Fill.SetBackgroundColor(XLColor.Red);
        sheet.Range("D1:D2").AddConditionalFormat().WhenGreaterThan(1).Fill.SetBackgroundColor(XLColor.Blue);

        var inspection = JsonSerializer.SerializeToElement(ClosedXMLPlugin.InspectCells(sheet, "A1", "B2"));

        Assert.Equal("3", inspection.GetProperty("callValuye").GetString());
        Assert.Equal("3", inspection.GetProperty("value").GetString());
        Assert.Equal("1+2", inspection.GetProperty("formula").GetString());
        var cells = inspection.GetProperty("cells");
        Assert.Equal(4, cells.GetArrayLength());
        Assert.True(cells[0].GetProperty("style").GetProperty("font").GetProperty("bold").GetBoolean());
        Assert.Equal(30d, inspection.GetProperty("rows")[0].GetProperty("height").GetDouble());
        Assert.Equal(20d, inspection.GetProperty("columns")[1].GetProperty("width").GetDouble());
        Assert.Single(inspection.GetProperty("merges").EnumerateArray());
        Assert.Single(inspection.GetProperty("conditionalRules").EnumerateArray());
    }

    [Theory]
    [InlineData("A10001", "value")]
    [InlineData("A10001", "formula")]
    [InlineData("A10001", "formatOnly")]
    [InlineData("XFD1048576", "value")]
    [InlineData("XFD1048576", "formula")]
    [InlineData("XFD1048576", "formatOnly")]
    public void Oversized_ranges_are_rejected_before_editing_or_inspection(string endpoint, string operation)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell("A1").Value = "original";
        sheet.Cell("A2").FormulaA1 = "1+2";
        sheet.Cell("A1").Style.Font.Italic = true;

        Assert.Throws<ArgumentException>(() => ClosedXMLPlugin.EditCells(sheet, "A1", "replacement",
            operation, endpoint, new() { Font = new() { Bold = true } }, "2+3"));
        Assert.Throws<ArgumentException>(() => ClosedXMLPlugin.InspectCells(sheet, "A1", endpoint));

        Assert.Equal("original", sheet.Cell("A1").GetString());
        Assert.Equal("1+2", sheet.Cell("A2").FormulaA1);
        Assert.True(sheet.Cell("A1").Style.Font.Italic);
        Assert.False(sheet.Cell("A1").Style.Font.Bold);
        Assert.Equal(2, sheet.CellsUsed().Count());
    }

    [Fact]
    public void Exactly_ten_thousand_cells_can_be_edited_and_inspected()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");

        ClosedXMLPlugin.EditCells(sheet, "A1", "boundary", endCellAddress: "CV100");
        var inspection = JsonSerializer.SerializeToElement(ClosedXMLPlugin.InspectCells(sheet, "A1", "CV100"));

        Assert.Equal(10_000, sheet.CellsUsed().Count());
        Assert.Equal("boundary", sheet.Cell("CV100").GetString());
        Assert.Equal(10_000, inspection.GetProperty("cells").GetArrayLength());
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("formula", null)]
    [InlineData("formula", " ")]
    public void Invalid_operation_or_formula_fails_before_editing(string operation, string? formula)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        sheet.Cell("A1").Value = "original";

        Assert.Throws<ArgumentException>(() =>
            ClosedXMLPlugin.EditCells(sheet, "A1", "replacement", operation, formula: formula));

        Assert.Equal("original", sheet.Cell("A1").GetString());
    }
}
