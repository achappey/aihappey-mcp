using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MCPhappey.Tools.OpenXML.PowerPoint;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace MCPhappey.Tests;

public sealed class OpenXmlPowerPointTests
{
    [Fact]
    public void Legacy_parameters_remain_in_their_original_positions_and_default_to_legacy_mode()
    {
        var parameters = typeof(PowerPointPlugin).GetMethod(nameof(PowerPointPlugin.OpenXMLPowerPoint_AddContent))!.GetParameters();
        Assert.Equal("cancellationToken", parameters[8].Name);
        Assert.Equal("formattingMode", parameters[9].Name);
        Assert.Equal("default", parameters[9].DefaultValue);
        Assert.All(parameters.Skip(9), parameter => Assert.True(parameter.IsOptional));
        RoundTrip(shape =>
        {
            typeof(PowerPointPlugin).GetMethod("SetShapeText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [shape, new[] { "legacy" }, false, true]);
            var paragraph = Assert.Single(shape.TextBody!.Elements<A.Paragraph>());
            Assert.Null(paragraph.ParagraphProperties);
            Assert.Null(Assert.Single(paragraph.Elements<A.Run>()).RunProperties);
        }, shape => Assert.Equal("legacy", shape.TextBody!.InnerText));
    }

    [Fact]
    public void Positional_replacement_keeps_mixed_runs_and_paragraph_body_properties()
    {
        RoundTrip(shape =>
        {
            var bodyProperties = shape.TextBody!.BodyProperties!.OuterXml;
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            var paragraphProperties = paragraph.ParagraphProperties!.OuterXml;
            var endProperties = paragraph.GetFirstChild<A.EndParagraphRunProperties>()!.OuterXml;
            var runProperties = paragraph.Elements<A.Run>().Select(r => r.RunProperties!.OuterXml).ToArray();

            Edit(shape, "ABCDEFG overflow\nkeep", replace: true);

            paragraph = shape.TextBody.Elements<A.Paragraph>().First();
            Assert.Equal(bodyProperties, shape.TextBody.BodyProperties.OuterXml);
            Assert.Equal(paragraphProperties, paragraph.ParagraphProperties!.OuterXml);
            Assert.Equal(endProperties, paragraph.GetFirstChild<A.EndParagraphRunProperties>()!.OuterXml);
            Assert.Equal(new[] { "ABC", "DEFG overflow" }, paragraph.Elements<A.Run>().Select(r => r.Text!.Text));
            Assert.Equal(runProperties, paragraph.Elements<A.Run>().Select(r => r.RunProperties!.OuterXml));
        }, shape => Assert.Equal("ABCDEFG overflowkeep", shape.TextBody!.InnerText));
    }

    [Fact]
    public void Noop_replacement_keeps_all_existing_XML()
    {
        RoundTrip(shape =>
        {
            var before = shape.OuterXml;
            Edit(shape, "abcdef\nkeep", replace: true);
            Assert.Equal(before, shape.OuterXml);
            Edit(shape, "bcde", paragraphIndex: 0, start: 1, length: 4);
            Assert.Equal(before, shape.OuterXml);
        });
    }

    [Fact]
    public void Format_only_splits_range_and_preserves_unrelated_XML_and_inheritance()
    {
        RoundTrip(shape =>
        {
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            var firstProperties = paragraph.Elements<A.Run>().First().RunProperties!.OuterXml;
            var secondProperties = paragraph.Elements<A.Run>().Last().RunProperties!.OuterXml;
            var unrelated = shape.TextBody.Elements<A.Paragraph>().Last().OuterXml;

            Edit(shape, "ignored", operation: "formatOnly", paragraphIndex: 0, start: 1, length: 4,
                formatting: new() { Italic = false, Color = "#1122aa", FontSize = 24, Underline = PowerPointUnderline.Double });

            paragraph = shape.TextBody.Elements<A.Paragraph>().First();
            var runs = paragraph.Elements<A.Run>().ToArray();
            Assert.Equal(new[] { "a", "bc", "de", "f" }, runs.Select(r => r.Text!.Text));
            Assert.Equal(firstProperties, runs[0].RunProperties!.OuterXml);
            Assert.Equal(secondProperties, runs[3].RunProperties!.OuterXml);
            Assert.True(runs[1].RunProperties!.Bold!.Value);
            Assert.False(runs[2].RunProperties!.Italic!.Value);
            Assert.Equal("1122AA", runs[1].RunProperties!.GetFirstChild<A.SolidFill>()!.GetFirstChild<A.RgbColorModelHex>()!.Val!.Value);
            Assert.Equal(2400, runs[2].RunProperties!.FontSize!.Value);
            Assert.Equal(unrelated, shape.TextBody.Elements<A.Paragraph>().Last().OuterXml);
        }, shape => Assert.Equal("abcdefkeep", shape.TextBody!.InnerText));
    }

    [Fact]
    public void Range_replacement_inherits_first_selected_run_and_preserves_suffix()
    {
        RoundTrip(shape =>
        {
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            var suffixProperties = paragraph.Elements<A.Run>().Last().RunProperties!.OuterXml;
            Edit(shape, " NEW ", paragraphIndex: 0, start: 1, length: 4,
                formatting: new() { FontFamily = "Aptos" });
            paragraph = shape.TextBody.Elements<A.Paragraph>().First();
            Assert.Equal("a NEW f", paragraph.InnerText);
            var runs = paragraph.Elements<A.Run>().ToArray();
            Assert.Equal(new[] { "a", " N", "EW ", "f" }, runs.Select(r => r.Text!.Text));
            Assert.True(runs[1].RunProperties!.Bold!.Value);
            Assert.Equal("Aptos", runs[1].RunProperties!.GetFirstChild<A.LatinFont>()!.Typeface!.Value);
            Assert.Equal(suffixProperties, runs[^1].RunProperties!.OuterXml);
        });
    }

    [Fact]
    public void Nonempty_range_at_run_boundary_inherits_selected_run_not_preceding_run()
    {
        RoundTrip(shape =>
        {
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            var precedingXml = paragraph.Elements<A.Run>().First().OuterXml;
            Edit(shape, "X", paragraphIndex: 0, start: 3, length: 2);
            paragraph = shape.TextBody.Elements<A.Paragraph>().First();
            var runs = paragraph.Elements<A.Run>().ToArray();
            Assert.Equal(new[] { "abc", "X", "f" }, runs.Select(r => r.Text!.Text));
            Assert.Equal(precedingXml, runs[0].OuterXml);
            Assert.True(runs[1].RunProperties!.Italic!.Value);
            Assert.Null(runs[1].RunProperties!.Bold);
        }, shape => Assert.Equal("abcXfkeep", shape.TextBody!.InnerText));
    }

    [Fact]
    public void Insertion_and_regular_run_targeting_preserve_other_paragraphs()
    {
        RoundTrip(shape =>
        {
            var untouched = shape.TextBody!.Elements<A.Paragraph>().Last().OuterXml;
            Edit(shape, "!", paragraphIndex: 0, runIndex: 1, start: 1, length: 0);
            Assert.Equal("abcd!ef", shape.TextBody.Elements<A.Paragraph>().First().InnerText);
            Edit(shape, "ignored", operation: "formatOnly", paragraphIndex: 0, runIndex: 0,
                formatting: new() { Bold = false });
            Assert.False(shape.TextBody.Elements<A.Paragraph>().First().Elements<A.Run>().First().RunProperties!.Bold!.Value);
            Assert.Equal(untouched, shape.TextBody.Elements<A.Paragraph>().Last().OuterXml);
        });
    }

    [Fact]
    public void Appended_paragraphs_inherit_last_paragraph_and_run_properties()
    {
        RoundTrip(shape =>
        {
            var previous = shape.TextBody!.Elements<A.Paragraph>().Last();
            var previousXml = previous.OuterXml;
            Edit(shape, "  padded  \n\nlast");
            var paragraphs = shape.TextBody.Elements<A.Paragraph>().ToArray();
            Assert.Equal(5, paragraphs.Length);
            Assert.Equal(previousXml, paragraphs[1].OuterXml);
            Assert.Equal("  padded  ", paragraphs[2].InnerText);
            Assert.Equal("", paragraphs[3].InnerText);
            Assert.Equal(previous.ParagraphProperties!.OuterXml, paragraphs[4].ParagraphProperties!.OuterXml);
            Assert.Equal(previous.Elements<A.Run>().Last().RunProperties!.OuterXml,
                paragraphs[4].Elements<A.Run>().First().RunProperties!.OuterXml);
        });
    }

    [Fact]
    public void Paragraph_alignment_does_not_add_or_change_character_properties()
    {
        RoundTrip(shape =>
        {
            var before = shape.TextBody!.Elements<A.Paragraph>().First().Elements<A.Run>().Select(r => r.OuterXml).ToArray();
            Edit(shape, "", operation: "formatOnly", paragraphIndex: 0,
                formatting: new() { Alignment = PowerPointAlignment.Center });
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            Assert.Equal(A.TextAlignmentTypeValues.Center, paragraph.ParagraphProperties!.Alignment!.Value);
            Assert.Equal(before, paragraph.Elements<A.Run>().Select(r => r.OuterXml));
        });
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, -1)]
    [InlineData(6, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Invalid_ranges_are_atomic(int start, int length)
    {
        RoundTrip(shape =>
        {
            var before = shape.OuterXml;
            Assert.Throws<ArgumentOutOfRangeException>(() => Edit(shape, "x", paragraphIndex: 0, start: start, length: length));
            Assert.Equal(before, shape.OuterXml);
        });
    }

    [Fact]
    public void Complex_ranges_and_invalid_patch_are_rejected_without_mutation()
    {
        RoundTrip(shape =>
        {
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            paragraph.InsertAfter(new A.Break(), paragraph.Elements<A.Run>().First());
            var before = shape.OuterXml;
            Assert.Throws<NotSupportedException>(() => Edit(shape, "x", paragraphIndex: 0, start: 0, length: 1));
            Assert.Throws<NotSupportedException>(() => Edit(shape, "new", replace: true));
            Assert.Throws<ArgumentException>(() => Edit(shape, "", formatting: new() { Color = "red" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Edit(shape, "", formatting: new() { FontSize = double.NaN }));
            Assert.Equal(before, shape.OuterXml);
        });
    }

    [Fact]
    public void Surrogate_pairs_cannot_be_split_by_range_or_positional_replacement()
    {
        RoundTrip(shape =>
        {
            Edit(shape, "ab😀zz", paragraphIndex: 0);
            var paragraph = shape.TextBody!.Elements<A.Paragraph>().First();
            Assert.Equal(new[] { "ab", "😀zz" }, paragraph.Elements<A.Run>().Select(r => r.Text!.Text));
            var before = shape.OuterXml;
            Assert.Throws<ArgumentException>(() => Edit(shape, "x", paragraphIndex: 0, start: 3, length: 0));
            Assert.Equal(before, shape.OuterXml);
        });
    }

    [Fact]
    public void Formatting_nontext_shapes_is_rejected_and_replacement_can_create_a_valid_body()
    {
        RoundTrip(shape =>
        {
            shape.TextBody!.Remove();
            var before = shape.OuterXml;
            Assert.Throws<InvalidOperationException>(() => Edit(shape, "", operation: "formatOnly", formatting: new() { Bold = true }));
            Assert.Equal(before, shape.OuterXml);
            Edit(shape, "created", operation: "replaceContent", formatting: new() { Bold = true });
        }, shape => Assert.Equal("created", shape.TextBody!.InnerText));
    }

    [Fact]
    public void Inspection_exposes_boundaries_properties_and_raw_theme_references()
    {
        RoundTrip(shape =>
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(PowerPointTextEditor.Inspect(shape, 3)));
            var inspected = json.RootElement;
            Assert.Equal(3, inspected.GetProperty("shapeIndex").GetInt32());
            Assert.Contains("anchor=\"ctr\"", inspected.GetProperty("bodyPropertiesXml").GetString());
            var paragraph = inspected.GetProperty("paragraphs")[0];
            Assert.Equal(6, paragraph.GetProperty("length").GetInt32());
            var runs = paragraph.GetProperty("runs");
            Assert.Equal(0, runs[0].GetProperty("start").GetInt32());
            Assert.Equal(3, runs[1].GetProperty("start").GetInt32());
            Assert.Equal(6, runs[1].GetProperty("end").GetInt32());
            Assert.True(runs[0].GetProperty("properties").GetProperty("bold").GetBoolean());
            Assert.Contains("schemeClr", runs[0].GetProperty("properties").GetProperty("rawXml").GetString());
            Assert.Equal("accent1", runs[0].GetProperty("properties").GetProperty("themeColor").GetString());
        });
    }

    private static void Edit(P.Shape shape, string content, bool replace = false, string? operation = null,
        int? paragraphIndex = null, int? runIndex = null, int? start = null, int? length = null,
        PowerPointFormattingPatch? formatting = null) =>
        PowerPointTextEditor.Edit(shape, content, replace, operation, paragraphIndex, runIndex, start, length, formatting);

    private static void RoundTrip(Action<P.Shape> edit, Action<P.Shape>? verify = null)
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            var presentation = document.AddPresentationPart();
            var slide = presentation.AddNewPart<SlidePart>();
            slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(), CreateShape())));
            presentation.Presentation = new P.Presentation(
                new P.SlideIdList(new P.SlideId { Id = 256, RelationshipId = presentation.GetIdOfPart(slide) }),
                new P.SlideSize { Cx = 9144000, Cy = 6858000 }, new P.NotesSize { Cx = 6858000, Cy = 9144000 });
            var shape = slide.Slide!.Descendants<P.Shape>().Single();
            edit(shape);
            slide.Slide.Save();
            presentation.Presentation.Save();
        }
        stream.Position = 0;
        using var reopened = PresentationDocument.Open(stream, false);
        var reopenedShape = reopened.PresentationPart!.SlideParts.Single().Slide!.Descendants<P.Shape>().Single();
        Assert.NotNull(reopenedShape.TextBody);
        Assert.Empty(new OpenXmlValidator().Validate(reopenedShape));
        verify?.Invoke(reopenedShape);
    }

    private static P.Shape CreateShape() => new(
        new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 2, Name = "Body" },
            new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
        new P.ShapeProperties(),
        new P.TextBody(
            new A.BodyProperties { Anchor = A.TextAnchoringTypeValues.Center, LeftInset = 500 },
            new A.ListStyle(),
            new A.Paragraph(
                new A.ParagraphProperties(new A.CharacterBullet { Char = "•" }) { Level = 1 },
                new A.Run(new A.RunProperties(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.Accent1 }),
                    new A.LatinFont { Typeface = "+mn-lt" }) { Bold = true }, new A.Text("abc")),
                new A.Run(new A.RunProperties { Italic = true }, new A.Text("def")),
                new A.EndParagraphRunProperties { Language = "nl-NL" }),
            new A.Paragraph(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Right },
                new A.Run(new A.RunProperties { FontSize = 1800 }, new A.Text("keep")))));
}
