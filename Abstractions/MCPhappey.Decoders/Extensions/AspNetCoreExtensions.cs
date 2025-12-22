using Microsoft.KernelMemory;
using OpenAI;

namespace MCPhappey.Decoders.Extensions;

public static class AspNetCoreExtensions
{
    private static readonly Dictionary<string, string> _audioTypes = new()
            {
                {"audio/mpeg", ".mp3" },
                {"audio/x-wav", ".wav"},
                {"audio/mp4", ".m4a"},
                {"audio/ogg", ".ogg"},
            };
    public static IKernelMemoryBuilder WithDecoders(
        this IKernelMemoryBuilder builder, OpenAIClient openAIClient)
    {
        foreach (var audioType in _audioTypes)
        {
            builder.WithContentDecoder(new AudioDecoder(openAIClient, audioType.Key, audioType.Value));
        }

        return builder.WithContentDecoder<EpubDecoder>()
            .WithContentDecoder<JsonDecoder>()
            .WithContentDecoder<RtfDecoder>()
            .WithContentDecoder<EmlDecoder>()
            .WithContentDecoder<HtmlDecoder>();
    }
}