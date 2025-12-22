using System.ComponentModel;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.ZstdSharp;

public static class ZstdSharpService
{
    [Description("Compress a file from URL using ZstdSharp.")]
    [McpServerTool(Name = "zstd_compress_file", ReadOnly = false)]
    public static async Task<CallToolResult?> Zstd_CompressFile(
    IServiceProvider serviceProvider,
    RequestContext<CallToolRequestParams> requestContext,
    [Description("File URL to compress.")] string fileUrl,
    [Description("Compression level (1–22, default 5).")] int level = 5,
    CancellationToken cancellationToken = default) =>
        await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graph =>
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
            fileUrl);
        var file = files.FirstOrDefault();
        using var compressor = new global::ZstdSharp.Compressor(level);
        var compressedSpan = compressor.Wrap(file?.Contents.ToArray());

        var compressedBytes = compressedSpan.ToArray(); // ✅ convert Span<byte> → byte[]

        using var ms = new MemoryStream(compressedBytes);
        var uploaded = await graph.Upload(
            $"{Path.GetFileNameWithoutExtension(file?.Filename)}.zst",
            await BinaryData.FromStreamAsync(ms, cancellationToken),
            cancellationToken);

        return uploaded?.ToCallToolResult();
    }));

    [Description("Decompress a .zst file from URL using ZstdSharp.")]
    [McpServerTool(Name = "zstd_decompress_file", ReadOnly = false)]
    public static async Task<CallToolResult?> Zstd_DecompressFile(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("URL of the .zst file to decompress.")] string fileUrl,
        CancellationToken cancellationToken = default) =>
            await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithOboGraphClient(async graph =>
            {
                var fileName = Path.GetFileNameWithoutExtension(fileUrl);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server,
                fileUrl);
                var file = files.FirstOrDefault();

                using var decompressor = new global::ZstdSharp.Decompressor();
                var decompressedSpan = decompressor.Unwrap(file?.Contents.ToArray());
                var decompressedBytes = decompressedSpan.ToArray(); // ✅ convert Span<byte> → byte[]

                using var ms = new MemoryStream(decompressedBytes);
                var uploaded = await graph.Upload(
                    $"{file?.Filename}.bin",
                    await BinaryData.FromStreamAsync(ms, cancellationToken),
                    cancellationToken);

                return uploaded?.ToResourceLinkCallToolResponse();
            }));

}
