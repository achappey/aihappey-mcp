using HtmlAgilityPack;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Decoders;

public class HtmlDecoder : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               (mimeType.Equals(MimeTypes.Html, StringComparison.OrdinalIgnoreCase) ||
                mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
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
        // Read HTML content from the stream
        using var reader = new StreamReader(data);
        var html = await reader.ReadToEndAsync(cancellationToken);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove <script> and <style> nodes
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        // Extract text content from all elements
        string extractedText = string.Join(
            "\n",
            doc.DocumentNode.Descendants()
                .Where(n => n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(n.InnerText))
                .Select(n => n.InnerText.Trim())
        );

        var result = new FileContent(MimeTypes.PlainText);
        result.Sections.Add(new Chunk(extractedText, 0, Chunk.Meta(sentencesAreComplete: true)));

        return result;
    }
}