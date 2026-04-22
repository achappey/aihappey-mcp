using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;

using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Decoders;

public class PptmDecoder : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               (mimeType.Equals("application/vnd.ms-powerpoint.presentation.macroEnabled.12", StringComparison.OrdinalIgnoreCase) ||
                mimeType.Equals("application/vnd.openxmlformats-officedocument.presentationml.presentation", StringComparison.OrdinalIgnoreCase)); // fallback
    }

    public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filename);
        return await DecodeAsync(stream, cancellationToken);
    }

    public async Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        await using var stream = data.ToStream();
        return await DecodeAsync(stream, cancellationToken);
    }

    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        // OpenXML is sync, dus even in memory trekken (zoals je JSON decoder)
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        var sb = new System.Text.StringBuilder();

        using (var doc = PresentationDocument.Open(ms, false))
        {
            var presentationPart = doc.PresentationPart;

            if (presentationPart?.SlideParts == null)
                return Empty();

            int slideIndex = 0;

            foreach (var slidePart in presentationPart.SlideParts)
            {
                slideIndex++;

                var texts = slidePart.Slide.Descendants<A.Text>();

                foreach (var t in texts)
                {
                    var text = t.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                }

                sb.AppendLine(); // spacing tussen slides
            }
        }

        var result = new FileContent(MimeTypes.PlainText);
        result.Sections.Add(new Chunk(sb.ToString(), 0, Chunk.Meta(sentencesAreComplete: true)));

        return result;
    }

    private static FileContent Empty()
    {
        var result = new FileContent(MimeTypes.PlainText);
        result.Sections.Add(new Chunk(string.Empty, 0, Chunk.Meta(sentencesAreComplete: true)));
        return result;
    }
}