using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Pipeline;
using OpenAI;
using OpenAI.Audio;

namespace MCPhappey.Decoders;

public class AudioDecoder(OpenAIClient openAIClient, string _mimeType, string extension) : IContentDecoder
{
    public bool SupportsMimeType(string mimeType)
        => mimeType.Equals(_mimeType, StringComparison.OrdinalIgnoreCase);

    public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filename);
        return DecodeAsync(stream, cancellationToken, filename);
    }

    public Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        return DecodeAsync(data.ToStream(), cancellationToken, $"audiofile.{extension}");
    }

    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        // Geen bestandsnaam bekend, geef standaardnaam
        return await DecodeAsync(data, cancellationToken, $"audiofile.{extension}");
    }

    private async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken, string filename)
    {
        AudioClient audioClient = openAIClient.GetAudioClient("gpt-4o-transcribe");

        // Transcribe
        var transcript = await audioClient.TranscribeAudioAsync(
            data,
            filename,
            new AudioTranscriptionOptions(),
            cancellationToken
        );

        var result = new FileContent(MimeTypes.PlainText);
        result.Sections.Add(new Chunk(transcript.Value.Text, 0, Chunk.Meta(sentencesAreComplete: true)));

        return result;
    }
}
