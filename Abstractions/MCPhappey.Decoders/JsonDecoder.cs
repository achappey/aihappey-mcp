using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;

namespace MCPhappey.Decoders;

public class JsonDecoder : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               mimeType.Equals(MimeTypes.Json, StringComparison.OrdinalIgnoreCase); // fallback
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
        var binaryData = await BinaryData.FromStreamAsync(data, cancellationToken);

        var result = new FileContent(MimeTypes.Json);
        result.Sections.Add(new Chunk(binaryData.ToString(), 0, Chunk.Meta(sentencesAreComplete: true)));

        return result;
    }
}
