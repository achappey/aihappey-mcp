using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MCPhappey.Tools.OpenXML.Word;

public sealed class WordFormattingPatch
{
    [Description("Six-digit RGB hex without #, or auto; null leaves unchanged")]
    [JsonPropertyName("color")]
    public string? Color { get; set; }
    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }
    [Description("true = single underline, false = explicitly none")]
    [JsonPropertyName("underline")]
    public bool? Underline { get; set; }
    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; set; }
    [Description("Font size in points, positive half-point increments")]
    [JsonPropertyName("fontSize")]
    public double? FontSize { get; set; }
}

internal static class WordTextEditor
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private sealed record Selection(Paragraph Paragraph, int Start, int Length);

    internal static int Edit(MainDocumentPart main, string originalText, string replacementText,
        string operation = "replaceContent", string formattingMode = "preserve",
        int? paragraphIndex = null, int? start = null, int? length = null,
        WordFormattingPatch? formatting = null, string author = "MCP")
    {
        if (operation is not ("replaceContent" or "formatOnly"))
            throw new ArgumentException("operation must be replaceContent or formatOnly.");
        if (formattingMode is not ("preserve" or "default"))
            throw new ArgumentException("formattingMode must be preserve or default.");
        if (operation == "formatOnly" && formatting == null)
            throw new ArgumentException("formatOnly requires a formatting patch.");
        ValidatePatch(formatting);
        if (start.HasValue != length.HasValue || (start.HasValue && !paragraphIndex.HasValue))
            throw new ArgumentException("start and length require each other and paragraphIndex.");
        if (replacementText.IndexOfAny(['\r', '\n', '\t']) >= 0 && operation == "replaceContent")
            throw new ArgumentException("Replacement must be plain text without line breaks or tabs.");
        var paragraphs = main.Document.Body?.Descendants<Paragraph>().ToArray() ?? [];
        if (paragraphIndex is < 0 || paragraphIndex >= paragraphs.Length)
            throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
        var selections = new List<Selection>();
        foreach (var paragraph in paragraphIndex.HasValue ? [paragraphs[paragraphIndex.Value]] : paragraphs)
        {
            var text = PlainText(paragraph);
            var offset = start ?? (operation == "formatOnly" && paragraphIndex.HasValue && string.IsNullOrEmpty(originalText)
                ? 0 : string.IsNullOrEmpty(originalText)
                    ? throw new ArgumentException("originalText is required for text search.")
                    : text.IndexOf(originalText, StringComparison.OrdinalIgnoreCase));
            if (offset < 0 && !start.HasValue)
                continue;
            var size = length ?? (operation == "formatOnly" && paragraphIndex.HasValue && string.IsNullOrEmpty(originalText)
                ? text.Length : originalText.Length);
            if (offset < 0 || size <= 0 || offset > text.Length - size)
                throw new ArgumentOutOfRangeException(nameof(length), "Selection must be a nonempty range inside the paragraph.");
            if (SplitsSurrogate(text, offset) || SplitsSurrogate(text, offset + size))
                throw new ArgumentException("Selection splits a UTF-16 surrogate pair.");
            // Preflight every paragraph before mutating any: fields, revisions, hyperlinks,
            // content controls and non-text run children have ambiguous editing semantics.
            if (paragraph.ChildElements.Any(e => e is not ParagraphProperties && e is not Run)
                || paragraph.Elements<Run>().Any(r => r.ChildElements.Any(e => e is not RunProperties && e is not Text))
                || paragraph.Descendants<RunPropertiesChange>().Any()
                || paragraph.Descendants<ParagraphPropertiesChange>().Any())
                throw new ArgumentException("Selection is in a complex field/revision/non-text paragraph; no changes applied.");
            selections.Add(new Selection(paragraph, offset, size));
        }
        if (selections.Count == 0)
            throw new ArgumentException("No matching text found.");

        // Allocate new IDs without renumbering existing markup, including IDs in other parts.
        var roots = new List<OpenXmlElement> { main.Document };
        roots.AddRange(main.HeaderParts.Select(p => (OpenXmlElement)p.Header));
        roots.AddRange(main.FooterParts.Select(p => (OpenXmlElement)p.Footer));
        if (main.FootnotesPart?.Footnotes is { } footnotes) roots.Add(footnotes);
        if (main.EndnotesPart?.Endnotes is { } endnotes) roots.Add(endnotes);
        var nextId = roots.SelectMany(r => r.Descendants()).SelectMany(e => e.GetAttributes())
            .Where(a => a.LocalName == "id" && a.NamespaceUri == WordNamespace).Select(a => a.Value)
            .Select(v => BigInteger.TryParse(v, out var n) ? n : BigInteger.Zero)
            .DefaultIfEmpty().Max() + 1;
        foreach (var selection in selections)
        {
            var offset = 0;
            DeletedRun? deleted = null;
            foreach (var run in selection.Paragraph.Elements<Run>().ToArray())
            {
                var runLength = PlainText(run).Length;
                var from = Math.Max(0, selection.Start - offset);
                var to = Math.Min(runLength, selection.Start + selection.Length - offset);
                offset += runLength;
                if (from >= to) continue;
                if (from > 0) run.InsertBeforeSelf(Slice(run, 0, from));
                var selected = Slice(run, from, to - from);
                if (operation == "formatOnly")
                {
                    ApplyPatch(selected, formattingMode, formatting);
                    run.InsertBeforeSelf(selected);
                }
                else
                {
                    if (deleted == null)
                    {
                        deleted = new DeletedRun { Id = (nextId++).ToString(CultureInfo.InvariantCulture), Author = author, Date = DateTime.UtcNow };
                        run.InsertBeforeSelf(deleted);
                        if (replacementText.Length > 0)
                        {
                            var insertedText = (Run)run.CloneNode(false);
                            if (run.RunProperties != null) insertedText.Append(run.RunProperties.CloneNode(true));
                            insertedText.Append(new Text(replacementText) { Space = SpaceProcessingModeValues.Preserve });
                            ApplyPatch(insertedText, formattingMode, formatting);
                            deleted.InsertAfterSelf(new InsertedRun(insertedText)
                            {
                                Id = (nextId++).ToString(CultureInfo.InvariantCulture), Author = author, Date = DateTime.UtcNow
                            });
                        }
                    }
                    foreach (var text in selected.Elements<Text>().ToArray())
                    {
                        var deletion = new DeletedText(text.Text);
                        deletion.SetAttributes(text.GetAttributes());
                        text.InsertBeforeSelf(deletion);
                        text.Remove();
                    }
                    deleted.Append(selected);
                }
                if (to < runLength) run.InsertBeforeSelf(Slice(run, to, runLength - to));
                run.Remove();
            }
        }
        if (operation == "replaceContent")
        {
            var settings = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
            settings.Settings ??= new Settings();
            if (settings.Settings.GetFirstChild<TrackRevisions>() is { } tracking)
                tracking.Val = true;
            else
                settings.Settings.AddChild(new TrackRevisions(), true);
            settings.Settings.Save();
        }
        main.Document.Save();
        return selections.Count;
    }

    private static bool SplitsSurrogate(string text, int index)
        => index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]);

    private static string PlainText(OpenXmlElement element) => string.Concat(element.Descendants<Text>().Select(t => t.Text));

    private static Run Slice(Run source, int start, int length)
    {
        var result = (Run)source.CloneNode(false);
        if (source.RunProperties != null) result.Append(source.RunProperties.CloneNode(true));
        var offset = 0;
        foreach (var text in source.Elements<Text>())
        {
            var from = Math.Max(0, start - offset);
            var to = Math.Min(text.Text.Length, start + length - offset);
            if (from < to)
            {
                var copy = (Text)text.CloneNode(true);
                copy.Text = text.Text[from..to];
                if (from != 0 || to != text.Text.Length) copy.Space = SpaceProcessingModeValues.Preserve;
                result.Append(copy);
            }
            offset += text.Text.Length;
        }
        return result;
    }

    private static void ValidatePatch(WordFormattingPatch? patch)
    {
        if (patch == null) return;
        if (patch.Color != null && patch.Color != "auto" && !Regex.IsMatch(patch.Color, @"\A[0-9a-fA-F]{6}\z"))
            throw new ArgumentException("Color must be six-digit RGB hex or auto.");
        if (patch.FontFamily != null && string.IsNullOrWhiteSpace(patch.FontFamily))
            throw new ArgumentException("FontFamily cannot be blank.");
        if (patch.FontSize is { } size && (!double.IsFinite(size) || size <= 0 || size > 1638 || size * 2 != Math.Truncate(size * 2)))
            throw new ArgumentException("FontSize must be positive half-point increments, at most 1638 points.");
    }

    private static void ApplyPatch(Run run, string mode, WordFormattingPatch? patch)
    {
        if (mode == "default") run.RunProperties?.Remove();
        if (patch == null) return;
        var properties = run.RunProperties ??= new RunProperties();
        if (patch.Color != null) properties.Color = new Color { Val = patch.Color };
        if (patch.Bold is { } bold) properties.Bold = new Bold { Val = bold };
        if (patch.Underline is { } underline) properties.Underline = new Underline { Val = underline ? UnderlineValues.Single : UnderlineValues.None };
        if (patch.FontFamily is { } family)
            properties.RunFonts = new RunFonts { Ascii = family, HighAnsi = family, EastAsia = family, ComplexScript = family };
        if (patch.FontSize is { } size)
        {
            var halfPoints = (size * 2).ToString(CultureInfo.InvariantCulture);
            properties.FontSize = new FontSize { Val = halfPoints };
            properties.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
        }
    }

    internal static object Inspect(MainDocumentPart main, int startIndex, int count, bool includeSupportingXml)
    {
        if (startIndex < 0 || count is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(count));
        var paragraphs = main.Document.Body?.Descendants<Paragraph>().ToArray() ?? [];
        return new
        {
            totalParagraphs = paragraphs.Length,
            offsetUnit = "UTF-16; concatenated w:t text only (excludes deleted text, field instructions, tabs and breaks)",
            paragraphs = paragraphs.Skip(startIndex).Take(count).Select((p, index) =>
            {
                var offset = 0;
                return new
                {
                    paragraphIndex = startIndex + index, text = PlainText(p),
                    paragraphPropertiesXml = p.ParagraphProperties?.OuterXml,
                    styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value,
                    editable = !p.ChildElements.Any(e => e is not ParagraphProperties && e is not Run)
                        && !p.Elements<Run>().Any(r => r.ChildElements.Any(e => e is not RunProperties && e is not Text))
                        && !p.Descendants<RunPropertiesChange>().Any() && !p.Descendants<ParagraphPropertiesChange>().Any(),
                    runs = p.Descendants<Run>().Select((r, runIndex) =>
                    {
                        var text = PlainText(r);
                        var start = offset;
                        offset += text.Length;
                        return new { runIndex, start, length = text.Length, text,
                            runPropertiesXml = r.RunProperties?.OuterXml,
                            styleId = r.RunProperties?.RunStyle?.Val?.Value, xml = r.OuterXml };
                    }).ToArray(),
                    xml = p.OuterXml
                };
            }).ToArray(),
            supportingXml = includeSupportingXml ? new
            {
                styles = main.StyleDefinitionsPart?.Styles?.OuterXml,
                numbering = main.NumberingDefinitionsPart?.Numbering?.OuterXml,
                sections = main.Document.Descendants<SectionProperties>().Select(s => s.OuterXml).ToArray(),
                tables = main.Document.Descendants<Table>().Select(t => t.OuterXml).ToArray(),
                headers = main.HeaderParts.Select(p => new { relationshipId = main.GetIdOfPart(p), xml = p.Header.OuterXml }).ToArray(),
                footers = main.FooterParts.Select(p => new { relationshipId = main.GetIdOfPart(p), xml = p.Footer.OuterXml }).ToArray()
            } : null
        };
    }
}
