using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NAudio.Wave;

namespace MCPhappey.Tools.AI;

public static partial class AudioPlugin
{
    [Description("Concatenate multiple base64-encoded MP3 audio files into one single MP3 file.")]
    [McpServerTool(Title = "Concatenate MP3 files",
     Destructive = false,
     OpenWorld = false)]
    public static async Task<CallToolResult?> AudioPlugin_ConcatMp3Files(
        IServiceProvider serviceProvider,
        [Description("List of MP3 file urls to concatenate, in order.")]
        List<string> mp3FilesUrls,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
    {
        if (mp3FilesUrls == null || mp3FilesUrls.Count < 2)
            return "Provide at least two mp3 files to concatenate.".ToErrorCallToolResponse();

        using var outputStream = new MemoryStream();
        Mp3Frame? mp3Frame;
        foreach (var mp3FileUrl in mp3FilesUrls)
        {
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();
            var mp3RawFiles = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
               mp3FileUrl, cancellationToken);

            var mp3RawFile = mp3RawFiles.FirstOrDefault();
            if (mp3RawFile == null) continue;

            using var mp3Stream = new MemoryStream(mp3RawFile.Contents.ToArray());
            using var reader = new Mp3FileReader(mp3Stream);

            // Read MP3 frames and write them to output (excluding ID3v2 tags after first file)
            while ((mp3Frame = reader.ReadNextFrame()) != null)
            {
                outputStream.Write(mp3Frame.RawData, 0, mp3Frame.RawData.Length);
            }
        }

        var result = await requestContext.Server.Upload(serviceProvider,
                                    requestContext.ToOutputFileName("mp3"),
                                    await BinaryData.FromStreamAsync(outputStream,
                                        cancellationToken),
                                            cancellationToken);

        return result?.ToResourceLinkCallToolResponse();

    });
}

