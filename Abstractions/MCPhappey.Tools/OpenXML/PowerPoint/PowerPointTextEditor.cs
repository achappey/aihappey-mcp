using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace MCPhappey.Tools.OpenXML.PowerPoint;

public enum PowerPointUnderline { None, Single, Double }
public enum PowerPointAlignment { Left, Center, Right, Justify }

public sealed class PowerPointFormattingPatch
{
    [Description("Latin font family. Null preserves explicit/inherited fonts.")]
    public string? FontFamily { get; set; }
    [Description("Font size in points (1–4000).")]
    public double? FontSize { get; set; }
    [Description("Six-digit RGB color, optionally prefixed with #. Replaces only the run fill.")]
    public string? Color { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public PowerPointUnderline? Underline { get; set; }
    public bool? Strike { get; set; }
    [Description("BCP-47 language tag stored on the run.")]
    public string? Language { get; set; }
    [Description("Paragraph alignment; valid only for whole-paragraph/shape operations.")]
    public PowerPointAlignment? Alignment { get; set; }
}

internal static class PowerPointTextEditor
{
    internal static void Edit(P.Shape shape, string content, bool replace, string? operation,
        int? paragraphIndex, int? runIndex, int? start, int? length,
        PowerPointFormattingPatch? formatting, bool bullets = false)
    {
        if (operation is not (null or "replaceContent" or "formatOnly"))
            throw new ArgumentException("operation must be replaceContent or formatOnly.");
        ValidatePatch(formatting);
        if (runIndex.HasValue && !paragraphIndex.HasValue)
            throw new ArgumentException("runIndex requires paragraphIndex.");
        if (start.HasValue != length.HasValue || (start.HasValue && !paragraphIndex.HasValue))
            throw new ArgumentException("start and length must be supplied together with paragraphIndex.");
        if (formatting?.Alignment != null && (runIndex.HasValue || start.HasValue))
            throw new ArgumentException("Alignment requires a whole paragraph or shape.");
        if (operation == "formatOnly" && formatting == null)
            throw new ArgumentException("formatOnly requires a formatting patch.");

        // Work on a clone so invalid/unsupported selections cannot leave a partially edited body.
        var body = (P.TextBody?)shape.TextBody?.CloneNode(true)
            ?? new P.TextBody(new A.BodyProperties(), new A.ListStyle());
        var paragraphs = body.Elements<A.Paragraph>().ToList();
        var formatOnly = operation == "formatOnly";
        if (paragraphIndex.HasValue)
        {
            if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Count)
                throw new ArgumentOutOfRangeException(nameof(paragraphIndex));
            var paragraph = paragraphs[paragraphIndex.Value];
            if (!formatOnly && content.Contains('\n'))
                throw new NotSupportedException("Targeted replacement cannot introduce paragraph boundaries.");
            if (runIndex.HasValue || start.HasValue)
            {
                EditSelection(paragraph, content, formatOnly, runIndex, start, length, formatting);
            }
            else
            {
                if (!formatOnly)
                    ReplaceParagraph(paragraph, content);
                ApplyParagraph(paragraph, formatting);
            }
        }
        else if (formatOnly)
        {
            if (paragraphs.Count == 0)
                throw new InvalidOperationException("formatOnly requires an existing text paragraph.");
            foreach (var paragraph in paragraphs)
                ApplyParagraph(paragraph, formatting);
        }
        else
        {
            var lines = content.Split('\n');
            if (operation == "replaceContent" || replace)
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    var paragraph = i < paragraphs.Count ? paragraphs[i] : InheritParagraph(paragraphs.LastOrDefault(), bullets);
                    if (i >= paragraphs.Count)
                        body.Append(paragraph);
                    ReplaceParagraph(paragraph, lines[i]);
                    ApplyParagraph(paragraph, formatting);
                }
                foreach (var paragraph in paragraphs.Skip(lines.Length))
                    paragraph.Remove();
            }
            else
            {
                foreach (var line in lines)
                {
                    var paragraph = InheritParagraph(body.Elements<A.Paragraph>().LastOrDefault(), bullets);
                    ReplaceParagraph(paragraph, line);
                    ApplyParagraph(paragraph, formatting);
                    body.Append(paragraph);
                }
            }
        }

        if (shape.TextBody == null)
            shape.TextBody = body;
        else if (shape.TextBody.OuterXml != body.OuterXml)
            shape.TextBody = body;
    }

    private static void ValidatePatch(PowerPointFormattingPatch? patch)
    {
        if (patch == null) return;
        if (patch.FontSize.HasValue && (!double.IsFinite(patch.FontSize.Value) || patch.FontSize < 1 || patch.FontSize > 4000))
            throw new ArgumentOutOfRangeException(nameof(patch.FontSize));
        if (patch.FontFamily != null && string.IsNullOrWhiteSpace(patch.FontFamily))
            throw new ArgumentException("FontFamily cannot be empty.");
        if (patch.Language != null && string.IsNullOrWhiteSpace(patch.Language))
            throw new ArgumentException("Language cannot be empty.");
        if (patch.Color != null && !Regex.IsMatch(patch.Color, "^#?[0-9a-fA-F]{6}$"))
            throw new ArgumentException("Color must be a six-digit RGB value.");
        if (patch.Underline.HasValue && !Enum.IsDefined(patch.Underline.Value))
            throw new ArgumentException("Invalid underline.");
        if (patch.Alignment.HasValue && !Enum.IsDefined(patch.Alignment.Value))
            throw new ArgumentException("Invalid alignment.");
    }

    private static A.Paragraph InheritParagraph(A.Paragraph? source, bool bullets)
    {
        var paragraph = new A.Paragraph();
        if (source?.ParagraphProperties != null)
            paragraph.ParagraphProperties = (A.ParagraphProperties)source.ParagraphProperties.CloneNode(true);
        else if (bullets)
            paragraph.ParagraphProperties = new A.ParagraphProperties(new A.CharacterBullet { Char = "•" });
        var run = new A.Run();
        var properties = source?.Elements<A.Run>().LastOrDefault()?.RunProperties;
        if (properties != null)
            run.RunProperties = (A.RunProperties)properties.CloneNode(true);
        run.Text = new A.Text("");
        paragraph.Append(run);
        if (source?.GetFirstChild<A.EndParagraphRunProperties>() is { } endProperties)
            paragraph.Append(endProperties.CloneNode(true));
        return paragraph;
    }

    private static void RequireSimple(A.Paragraph paragraph)
    {
        if (paragraph.ChildElements.Any(e => e is not (A.ParagraphProperties or A.Run or A.EndParagraphRunProperties)) ||
            paragraph.Elements<A.Run>().Any(r => r.ChildElements.Any(e => e is not (A.RunProperties or A.Text)) ||
                r.Elements<A.Text>().Count() != 1))
            throw new NotSupportedException("Replacement/ranges support ordinary text runs only; fields, breaks, and complex XML are not rewritten.");
    }

    private static void ReplaceParagraph(A.Paragraph paragraph, string text)
    {
        RequireSimple(paragraph);
        var runs = paragraph.Elements<A.Run>().ToList();
        if (runs.Count == 0)
        {
            var run = new A.Run(new A.Text(text));
            paragraph.InsertBefore(run, paragraph.GetFirstChild<A.EndParagraphRunProperties>());
            return;
        }
        var offset = 0;
        for (var i = 0; i < runs.Count; i++)
        {
            var count = i == runs.Count - 1 ? text.Length - offset : Math.Min(runs[i].Text!.Text.Length, text.Length - offset);
            // A replacement may move a surrogate pair across an old run boundary.
            if (offset + count < text.Length && count > 0 && char.IsHighSurrogate(text[offset + count - 1]) &&
                char.IsLowSurrogate(text[offset + count]))
                count--;
            var value = text.Substring(offset, count);
            if (runs[i].Text!.Text != value)
                runs[i].Text!.Text = value;
            offset += count;
        }
    }

    private static void EditSelection(A.Paragraph paragraph, string content, bool formatOnly,
        int? runIndex, int? start, int? length, PowerPointFormattingPatch? patch)
    {
        var runs = paragraph.Elements<A.Run>().ToList();
        if (runIndex.HasValue && (runIndex < 0 || runIndex >= runs.Count))
            throw new ArgumentOutOfRangeException(nameof(runIndex));
        if (runIndex.HasValue && !start.HasValue)
        {
            var selected = runs[runIndex.Value];
            if (!formatOnly)
            {
                if (selected.ChildElements.Any(e => e is not (A.RunProperties or A.Text)) || selected.Elements<A.Text>().Count() != 1)
                    throw new NotSupportedException("Run replacement requires a single ordinary text node.");
                selected.Text!.Text = content;
            }
            ApplyRun(selected, patch);
            return;
        }

        RequireSimple(paragraph);
        var selectedRuns = runIndex.HasValue ? new List<A.Run> { runs[runIndex.Value] } : runs;
        var text = string.Concat(selectedRuns.Select(r => r.Text!.Text));
        var from = start!.Value;
        var count = length!.Value;
        if (from < 0 || count < 0 || from > text.Length || count > text.Length - from)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (SplitsSurrogate(text, from) || SplitsSurrogate(text, from + count))
            throw new ArgumentException("A range cannot split a UTF-16 surrogate pair.");
        if (formatOnly && count == 0) return;
        if (!formatOnly && !HasCharacterPatch(patch) && text.Substring(from, count) == content) return;
        var offset = 0;
        var inserted = false;
        var replacementOffset = 0;
        foreach (var run in selectedRuns)
        {
            var value = run.Text!.Text;
            var end = offset + value.Length;
            var overlaps = offset < from + count && end > from;
            var insertsHere = !formatOnly && count == 0 && !inserted && from >= offset && from <= end;
            if (!overlaps && !insertsHere)
            {
                offset = end;
                continue;
            }
            var localStart = Math.Clamp(from - offset, 0, value.Length);
            var localEnd = Math.Clamp(from + count - offset, 0, value.Length);
            if (localStart > 0)
                run.InsertBeforeSelf(CloneRun(run, value[..localStart], null));
            if (formatOnly)
                run.InsertBeforeSelf(CloneRun(run, value[localStart..localEnd], patch));
            else
            {
                var replacementCount = insertsHere || from + count <= end
                    ? content.Length - replacementOffset
                    : Math.Min(localEnd - localStart, content.Length - replacementOffset);
                if (SplitsSurrogate(content, replacementOffset + replacementCount))
                    replacementCount--;
                run.InsertBeforeSelf(CloneRun(run, content.Substring(replacementOffset, replacementCount), patch));
                replacementOffset += replacementCount;
                inserted = true;
            }
            if (localEnd < value.Length)
                run.InsertBeforeSelf(CloneRun(run, value[localEnd..], null));
            run.Remove();
            offset = end;
        }
        if (!formatOnly && !inserted)
        {
            var run = new A.Run(new A.Text(content));
            ApplyRun(run, patch);
            paragraph.InsertBefore(run, paragraph.GetFirstChild<A.EndParagraphRunProperties>());
        }
    }

    private static bool SplitsSurrogate(string text, int position) =>
        position > 0 && position < text.Length && char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position]);

    private static A.Run CloneRun(A.Run source, string text, PowerPointFormattingPatch? patch)
    {
        var clone = (A.Run)source.CloneNode(true);
        clone.Text!.Text = text;
        ApplyRun(clone, patch);
        return clone;
    }

    private static void ApplyParagraph(A.Paragraph paragraph, PowerPointFormattingPatch? patch)
    {
        if (patch == null) return;
        foreach (var run in paragraph.Elements<A.Run>())
            ApplyRun(run, patch);
        // Fields and breaks keep their own text/XML but can carry the same character patch.
        foreach (var element in paragraph.ChildElements.Where(e => HasCharacterPatch(patch) && e is (A.Field or A.Break)))
        {
            var properties = element.GetFirstChild<A.RunProperties>() ?? element.PrependChild(new A.RunProperties());
            ApplyProperties(properties, patch);
        }
        if (HasCharacterPatch(patch) && !paragraph.Elements<A.Run>().Any())
        {
            paragraph.ParagraphProperties ??= new A.ParagraphProperties();
            var defaults = paragraph.ParagraphProperties.GetFirstChild<A.DefaultRunProperties>()
                ?? paragraph.ParagraphProperties.AppendChild(new A.DefaultRunProperties());
            ApplyProperties(defaults, patch);
            var endProperties = paragraph.GetFirstChild<A.EndParagraphRunProperties>()
                ?? paragraph.AppendChild(new A.EndParagraphRunProperties());
            ApplyProperties(endProperties, patch);
        }
        if (patch.Alignment.HasValue)
        {
            paragraph.ParagraphProperties ??= new A.ParagraphProperties();
            Set(paragraph.ParagraphProperties, "algn", patch.Alignment.Value switch
            {
                PowerPointAlignment.Left => "l",
                PowerPointAlignment.Center => "ctr",
                PowerPointAlignment.Right => "r",
                _ => "just"
            });
        }
    }

    private static void ApplyRun(A.Run run, PowerPointFormattingPatch? patch)
    {
        if (!HasCharacterPatch(patch)) return;
        run.RunProperties ??= new A.RunProperties();
        ApplyProperties(run.RunProperties, patch!);
    }

    private static bool HasCharacterPatch(PowerPointFormattingPatch? patch) => patch != null &&
        (patch.FontFamily != null || patch.FontSize != null || patch.Color != null || patch.Bold != null ||
         patch.Italic != null || patch.Underline != null || patch.Strike != null || patch.Language != null);

    private static void ApplyProperties(OpenXmlCompositeElement properties, PowerPointFormattingPatch patch)
    {
        if (patch.Bold.HasValue) Set(properties, "b", patch.Bold.Value ? "1" : "0");
        if (patch.Italic.HasValue) Set(properties, "i", patch.Italic.Value ? "1" : "0");
        if (patch.Strike.HasValue) Set(properties, "strike", patch.Strike.Value ? "sngStrike" : "noStrike");
        if (patch.Underline.HasValue) Set(properties, "u", patch.Underline.Value switch
        {
            PowerPointUnderline.None => "none",
            PowerPointUnderline.Single => "sng",
            _ => "dbl"
        });
        if (patch.FontSize.HasValue)
            Set(properties, "sz", Math.Round(patch.FontSize.Value * 100).ToString(CultureInfo.InvariantCulture));
        if (patch.Language != null) Set(properties, "lang", patch.Language);
        if (patch.FontFamily != null)
        {
            var font = properties.GetFirstChild<A.LatinFont>();
            if (font == null)
                properties.AddChild(new A.LatinFont { Typeface = patch.FontFamily }, true);
            else
                font.Typeface = patch.FontFamily;
        }
        if (patch.Color != null)
        {
            foreach (var fill in properties.ChildElements.Where(e => e is A.NoFill or A.SolidFill or A.GradientFill or A.BlipFill or A.PatternFill or A.GroupFill).ToList())
                fill.Remove();
            properties.AddChild(new A.SolidFill(new A.RgbColorModelHex { Val = patch.Color.TrimStart('#').ToUpperInvariant() }), true);
        }
    }

    private static void Set(OpenXmlElement element, string name, string value) =>
        element.SetAttribute(new OpenXmlAttribute("", name, "", value));

    private static string? Attribute(OpenXmlElement? element, string name) =>
        element?.GetAttributes().FirstOrDefault(a => a.LocalName == name).Value;

    private static object Properties(OpenXmlElement? properties) => new
    {
        rawXml = properties?.OuterXml,
        fontFamily = properties?.GetFirstChild<A.LatinFont>()?.Typeface?.Value,
        fontSize = double.TryParse(Attribute(properties, "sz"), CultureInfo.InvariantCulture, out var size) ? (double?)(size / 100) : null,
        bold = Flag(properties, "b"),
        italic = Flag(properties, "i"),
        underline = Attribute(properties, "u"),
        strike = Attribute(properties, "strike"),
        language = Attribute(properties, "lang"),
        color = properties?.GetFirstChild<A.SolidFill>()?.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value,
        themeColor = properties?.GetFirstChild<A.SolidFill>()?.GetFirstChild<A.SchemeColor>()?.Val?.InnerText
    };

    private static bool? Flag(OpenXmlElement? element, string name) => Attribute(element, name) switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null
    };

    internal static object Inspect(P.Shape shape, int index)
    {
        var paragraphs = shape.TextBody?.Elements<A.Paragraph>().Select((paragraph, paragraphIndex) =>
        {
            var offset = 0;
            var runIndex = 0;
            var runs = paragraph.ChildElements.Where(e => e is A.Run or A.Field or A.Break).Select(element =>
            {
                var text = element is A.Break ? "\n" : element.InnerText;
                var start = offset;
                offset += text.Length;
                return new
                {
                    runIndex = element is A.Run ? (int?)runIndex++ : null,
                    kind = element.LocalName,
                    start,
                    length = text.Length,
                    end = offset,
                    text,
                    properties = Properties(element.GetFirstChild<A.RunProperties>()),
                    rawXml = element.OuterXml
                };
            }).ToList();
            return new
            {
                paragraphIndex,
                text = string.Concat(runs.Select(r => r.text)),
                length = offset,
                propertiesXml = paragraph.ParagraphProperties?.OuterXml,
                alignment = Attribute(paragraph.ParagraphProperties, "algn"),
                level = Attribute(paragraph.ParagraphProperties, "lvl"),
                defaultRunProperties = Properties(paragraph.ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>()),
                endParagraphRunProperties = Properties(paragraph.GetFirstChild<A.EndParagraphRunProperties>()),
                runs
            };
        }).ToList();
        return new
        {
            shapeIndex = index,
            name = shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value,
            placeholderType = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                .GetFirstChild<P.PlaceholderShape>()?.Type?.Value.ToString() ?? "None",
            placeholderXml = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<P.PlaceholderShape>()?.OuterXml,
            text = shape.TextBody?.InnerText?.Trim() ?? "",
            textBodyXml = shape.TextBody?.OuterXml,
            bodyPropertiesXml = shape.TextBody?.BodyProperties?.OuterXml,
            listStyleXml = shape.TextBody?.ListStyle?.OuterXml,
            styleXml = shape.ShapeStyle?.OuterXml,
            paragraphs
        };
    }
}
