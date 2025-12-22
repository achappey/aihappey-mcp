using System.Net.Mime;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;
using RtfPipe;

namespace MCPhappey.Decoders;

public class RtfDecoder : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               (mimeType.Equals(MediaTypeNames.Application.Rtf, StringComparison.OrdinalIgnoreCase) ||
                mimeType.Equals("text/rtf", StringComparison.OrdinalIgnoreCase) ||
                mimeType.Equals(".rtf", StringComparison.OrdinalIgnoreCase));
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
        using var reader = new StreamReader(data);
        var rtf = await reader.ReadToEndAsync(cancellationToken);

        // Gebruik RtfPipe om de plain text te extraheren
        var extractedText = Rtf.ToHtml(rtf);

        var result = new FileContent(MimeTypes.Html);
        result.Sections.Add(new Chunk(extractedText, 0, Chunk.Meta(sentencesAreComplete: true)));

        return result;
    }
}
