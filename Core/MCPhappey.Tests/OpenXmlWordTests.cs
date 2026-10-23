using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using MCPhappey.Tools.OpenXML.Word;

namespace MCPhappey.Tests;

public sealed class OpenXmlWordTests
{
    [Fact]
    public void New_editor_parameters_preserve_existing_positional_signature_and_patch_json_names()
    {
        var parameters = typeof(WordPlugin).GetMethod(nameof(WordPlugin.OpenXMLWord_ReplaceTextWithTrackChanges))!.GetParameters();
        Assert.Equal("cancellationToken", parameters[5].Name);
        Assert.Equal(["operation", "formattingMode", "paragraphIndex", "start", "length", "formatting"],
            parameters.Skip(6).Select(p => p.Name).ToArray());
        Assert.All(parameters.Skip(6), p => Assert.True(p.IsOptional));
        var patch = JsonSerializer.Deserialize<WordFormattingPatch>("""{"color":"123456","bold":true,"underline":false,"fontFamily":"Arial","fontSize":11.5}""")!;
        Assert.Equal("123456", patch.Color);
        Assert.True(patch.Bold);
        Assert.False(patch.Underline);
        Assert.Equal("Arial", patch.FontFamily);
        Assert.Equal(11.5, patch.FontSize);
    }

    [Fact]
    public void Cross_run_replacement_roundtrips_preserving_properties_and_unrelated_xml()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var before = new Run(new RunProperties(new Italic()), new Text("prefix "));
            var first = new Run(new RunProperties(new Bold(), new Color { Val = "123456" }), new Text("Hel"));
            var second = new Run(new RunProperties(new Italic()), new Text("LO"));
            var after = new Run(new RunProperties(new RunStyle { Val = "Emphasis" }), new Text(" suffix"));
            var pPr = new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" });
            var paragraph = new Paragraph(pPr, before, first, second, after);
            var unrelated = new Paragraph(new InsertedRun(new Run(new Text("existing"))) { Id = "88", Author = "Original" });
            main.Document = new Document(new Body(paragraph, unrelated));
            var expectedBefore = before.OuterXml;
            var expectedAfter = after.OuterXml;
            var expectedProperties = pPr.OuterXml;
            var expectedUnrelated = unrelated.OuterXml;

            Assert.Equal(1, WordTextEditor.Edit(main, "hello", "World"));

            Assert.Same(before, paragraph.Elements<Run>().First());
            Assert.Same(after, paragraph.Elements<Run>().Last());
            Assert.Equal(expectedBefore, before.OuterXml);
            Assert.Equal(expectedAfter, after.OuterXml);
            Assert.Equal(expectedProperties, pPr.OuterXml);
            Assert.Equal(expectedUnrelated, unrelated.OuterXml);
            var deleted = Assert.Single(paragraph.Elements<DeletedRun>());
            Assert.Equal("HelLO", string.Concat(deleted.Descendants<DeletedText>().Select(t => t.Text)));
            Assert.Empty(deleted.Descendants<Text>());
            Assert.NotNull(deleted.Elements<Run>().First().RunProperties?.Bold);
            Assert.NotNull(deleted.Elements<Run>().Last().RunProperties?.Italic);
            var inserted = Assert.Single(paragraph.Elements<InsertedRun>());
            Assert.Equal("World", inserted.InnerText);
            Assert.Equal("123456", inserted.GetFirstChild<Run>()?.RunProperties?.Color?.Val?.Value);
            Assert.Equal("89", deleted.Id?.Value);
            Assert.Equal("90", inserted.Id?.Value);
            Assert.NotNull(main.DocumentSettingsPart?.Settings?.GetFirstChild<TrackRevisions>());
            // Style references need not resolve for schema validation.
            Assert.Empty(new OpenXmlValidator().Validate(document));
        }
        stream.Position = 0;
        using var reopened = WordprocessingDocument.Open(stream, false);
        var reopenedParagraph = reopened.MainDocumentPart!.Document.Body!.Elements<Paragraph>().First();
        Assert.Equal("HelLO", string.Concat(reopenedParagraph.Descendants<DeletedText>().Select(t => t.Text)));
        Assert.Equal("World", Assert.Single(reopenedParagraph.Elements<InsertedRun>()).InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(reopened));
    }

    [Fact]
    public void Formatting_only_preserves_text_settings_ids_and_unrelated_runs_after_roundtrip()
    {
        using var stream = new MemoryStream();
        string expectedText;
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            var target = new Run(new RunProperties(new RunStyle { Val = "Emphasis" }, new Italic()), new Text("abcdef"));
            var untouched = new Run(new RunProperties(new Bold()), new Text(" tail"));
            var paragraph = new Paragraph(target, untouched);
            var revision = new Paragraph(new DeletedRun(new Run(new DeletedText("old"))) { Id = "0007", Author = "Original" });
            main.Document = new Document(new Body(paragraph, revision));
            var settings = main.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new Settings(new TrackRevisions { Val = false });
            var settingsXml = settings.Settings.OuterXml;
            var revisionXml = revision.OuterXml;
            var untouchedXml = untouched.OuterXml;
            expectedText = string.Concat(main.Document.Descendants<Text>().Select(t => t.Text));

            WordTextEditor.Edit(main, "", "ignored", "formatOnly", paragraphIndex: 0, start: 1, length: 3,
                formatting: new WordFormattingPatch { Color = "ABCDEF", Bold = false, Underline = true, FontFamily = "Arial", FontSize = 12.5 });

            Assert.Equal(expectedText, string.Concat(main.Document.Descendants<Text>().Select(t => t.Text)));
            Assert.Equal(settingsXml, settings.Settings.OuterXml);
            Assert.Equal(revisionXml, revision.OuterXml);
            Assert.Same(untouched, paragraph.Elements<Run>().Last());
            Assert.Equal(untouchedXml, untouched.OuterXml);
            var formatted = paragraph.Elements<Run>().ElementAt(1);
            Assert.Equal("bcd", formatted.InnerText);
            Assert.Equal("Emphasis", formatted.RunProperties?.RunStyle?.Val?.Value);
            Assert.NotNull(formatted.RunProperties?.Italic);
            Assert.False(formatted.RunProperties?.Bold?.Val?.Value);
            Assert.Equal("25", formatted.RunProperties?.FontSize?.Val?.Value);
            Assert.Equal("Arial", formatted.RunProperties?.RunFonts?.Ascii?.Value);
            Assert.Equal(UnderlineValues.Single, formatted.RunProperties?.Underline?.Val?.Value);
            Assert.Empty(paragraph.Elements<InsertedRun>());
            Assert.Empty(new OpenXmlValidator().Validate(document));
        }
        stream.Position = 0;
        using var reopened = WordprocessingDocument.Open(stream, false);
        Assert.Equal(expectedText, string.Concat(reopened.MainDocumentPart!.Document.Descendants<Text>().Select(t => t.Text)));
        Assert.False(reopened.MainDocumentPart.DocumentSettingsPart?.Settings?.GetFirstChild<TrackRevisions>()?.Val?.Value);
    }

    [Fact]
    public void Formatting_without_settings_does_not_create_revision_settings()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new Run(new Text("one")), new Run(new Text("two"))));
        WordTextEditor.Edit(document.MainDocumentPart!, "net", "", "formatOnly", formatting: new WordFormattingPatch { Bold = true });
        Assert.Null(document.MainDocumentPart!.DocumentSettingsPart);
        Assert.Equal("onetwo", document.MainDocumentPart.Document.InnerText);
        Assert.Equal(2, document.MainDocumentPart.Document.Descendants<Run>().Count(r => r.RunProperties?.Bold != null));
    }

    [Fact]
    public void Replacement_splits_boundary_runs_but_preserves_original_properties()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(
            new Run(new RunProperties(new Bold()), new Text("before HEL")),
            new Run(new RunProperties(new Italic()), new Text("LO after"))));
        var main = document.MainDocumentPart!;
        WordTextEditor.Edit(main, "hello", "X");
        var paragraph = main.Document.Body!.GetFirstChild<Paragraph>()!;
        Assert.Equal("before ", paragraph.Elements<Run>().First().InnerText);
        Assert.NotNull(paragraph.Elements<Run>().First().RunProperties?.Bold);
        Assert.Equal(" after", paragraph.Elements<Run>().Last().InnerText);
        Assert.NotNull(paragraph.Elements<Run>().Last().RunProperties?.Italic);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Theory]
    [InlineData("replaceContent")]
    [InlineData("formatOnly")]
    public void Complex_selection_is_rejected_before_any_mutation(string operation)
    {
        using var stream = new MemoryStream();
        using var document = Create(stream,
            new Paragraph(new Run(new Text("target"))),
            new Paragraph(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new Text("target"))));
        var main = document.MainDocumentPart!;
        var original = main.Document.OuterXml;
        Assert.Throws<ArgumentException>(() => WordTextEditor.Edit(main, "target", "new", operation,
            formatting: new WordFormattingPatch { Bold = true }));
        Assert.Equal(original, main.Document.OuterXml);
        Assert.Null(main.DocumentSettingsPart);
    }

    [Fact]
    public void Existing_revision_selection_is_rejected_without_normalizing_ids()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new InsertedRun(new Run(new Text("target"))) { Id = "0005", Author = "Original" }));
        var main = document.MainDocumentPart!;
        var xml = main.Document.OuterXml;
        Assert.Throws<ArgumentException>(() => WordTextEditor.Edit(main, "target", "new"));
        Assert.Equal(xml, main.Document.OuterXml);
    }

    [Fact]
    public void Default_mode_clears_only_selected_direct_properties()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(new RunProperties(new Bold(), new Italic()), new Text("abc"))));
        WordTextEditor.Edit(document.MainDocumentPart!, "", "", "formatOnly", "default", 0, 1, 1,
            new WordFormattingPatch { Color = "123456" });
        var runs = document.MainDocumentPart!.Document.Descendants<Run>().ToArray();
        Assert.NotNull(runs[0].RunProperties?.Bold);
        Assert.Null(runs[1].RunProperties?.Bold);
        Assert.Equal("123456", runs[1].RunProperties?.Color?.Val?.Value);
        Assert.NotNull(runs[2].RunProperties?.Bold);
        Assert.Equal("abc", document.MainDocumentPart.Document.InnerText);
    }

    [Fact]
    public void Empty_replacement_deletes_exact_original_casing_across_multiple_text_nodes()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(
            new Run(new RunProperties(new Bold()), new Text("ab"), new Text("CD"), new Text("ef"))));
        var main = document.MainDocumentPart!;
        WordTextEditor.Edit(main, "bcde", "");
        var paragraph = main.Document.Body!.GetFirstChild<Paragraph>()!;
        Assert.Equal("a", paragraph.Elements<Run>().First().InnerText);
        Assert.Equal("f", paragraph.Elements<Run>().Last().InnerText);
        Assert.Equal("bCDe", string.Concat(paragraph.Descendants<DeletedText>().Select(t => t.Text)));
        Assert.Empty(paragraph.Elements<InsertedRun>());
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void Replacement_enables_existing_disabled_tracking_without_touching_other_settings()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new Run(new Text("target"))));
        var main = document.MainDocumentPart!;
        main.AddNewPart<DocumentSettingsPart>().Settings = new Settings(
            new TrackRevisions { Val = false }, new DefaultTabStop { Val = 720 });
        WordTextEditor.Edit(main, "target", "new");
        Assert.True(main.DocumentSettingsPart!.Settings!.GetFirstChild<TrackRevisions>()?.Val?.Value);
        Assert.Equal((short)720, main.DocumentSettingsPart.Settings.GetFirstChild<DefaultTabStop>()?.Val?.Value);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void Utf16_surrogate_boundary_is_rejected_before_changes()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new Run(new Text("a😀z"))));
        var main = document.MainDocumentPart!;
        var xml = main.Document.OuterXml;
        Assert.Throws<ArgumentException>(() => WordTextEditor.Edit(main, "", "x", paragraphIndex: 0, start: 2, length: 1));
        Assert.Equal(xml, main.Document.OuterXml);
    }

    [Fact]
    public void Inspection_exposes_boundaries_styles_and_supporting_xml()
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new Run(new RunProperties(new Bold()), new Text("ab")), new Run(new Text("cd"))));
        var main = document.MainDocumentPart!;
        main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(new Style { Type = StyleValues.Paragraph, StyleId = "Normal" });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(WordTextEditor.Inspect(main, 0, 100, true)));
        var paragraph = json.RootElement.GetProperty("paragraphs")[0];
        Assert.Equal("abcd", paragraph.GetProperty("text").GetString());
        Assert.Equal(2, paragraph.GetProperty("runs")[1].GetProperty("start").GetInt32());
        Assert.Contains("w:b", paragraph.GetProperty("runs")[0].GetProperty("runPropertiesXml").GetString());
        Assert.Contains("Normal", json.RootElement.GetProperty("supportingXml").GetProperty("styles").GetString());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    public void Invalid_range_does_not_mutate(int start, int length)
    {
        using var stream = new MemoryStream();
        using var document = Create(stream, new Paragraph(new Run(new Text("abc"))));
        var main = document.MainDocumentPart!;
        var xml = main.Document.OuterXml;
        Assert.Throws<ArgumentOutOfRangeException>(() => WordTextEditor.Edit(main, "", "x", paragraphIndex: 0, start: start, length: length));
        Assert.Equal(xml, main.Document.OuterXml);
    }

    private static WordprocessingDocument Create(Stream stream, params Paragraph[] paragraphs)
    {
        var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true);
        document.AddMainDocumentPart().Document = new Document(new Body(paragraphs));
        return document;
    }
}
