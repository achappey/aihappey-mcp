using System.Text;
using HtmlAgilityPack;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;
using VersOne.Epub;

namespace MCPhappey.Decoders;

public class EpubDecoder : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               (mimeType.Equals(MimeTypes.ElectronicPublicationZip, StringComparison.OrdinalIgnoreCase) ||
                mimeType.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)); // fallback
    }

    public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filename);
        return DecodeAsync(stream, cancellationToken);
    }

    public Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        using var stream = data.ToStream();
        return DecodeAsync(stream, cancellationToken);
    }

    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        var book = await EpubReader.ReadBookAsync(data);

        var sb = new StringBuilder();
        foreach (var textFile in book.ReadingOrder)
        {
            sb.AppendLine(PrintTextContentFile(textFile));
        }

        var result = new FileContent(MimeTypes.PlainText);
        result.Sections.Add(new Chunk(sb.ToString(), 0, Chunk.Meta(sentencesAreComplete: false)));

        return result;
    }

    private static string PrintTextContentFile(EpubLocalTextContentFile textContentFile)
    {
        HtmlDocument htmlDocument = new();
        htmlDocument.LoadHtml(textContentFile.Content);
        StringBuilder sb = new();
        var nodes = htmlDocument.DocumentNode.SelectNodes("//text()");

        if (nodes != null)
        {
            foreach (HtmlNode node in nodes)
            {
                sb.AppendLine(node.InnerText.Trim());
            }

        }

        return sb.ToString();
    }
}
