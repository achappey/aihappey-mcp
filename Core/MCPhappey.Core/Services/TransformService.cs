
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.KernelMemory.DataFormats;

namespace MCPhappey.Core.Services;

public class TransformService(
    IEnumerable<IContentDecoder> contentDecoders)
{
    public async Task<FileItem> DecodeAsync(string uri, BinaryData binaryData, string contentType, string? filename = null,
         CancellationToken cancellationToken = default)
    {
        if (contentType.StartsWith("image/")
            || contentType.Equals("text/html;profile=mcp-app", StringComparison.OrdinalIgnoreCase))
        {
            return new FileItem
            {
                Contents = binaryData,
                MimeType = contentType,
                Uri = uri,
                Filename = filename,
            };
        }

        string? myAssemblyName = typeof(TransformService).Namespace?.Split(".").FirstOrDefault();

        var bestDecoder = contentDecoders
            .ByMimeType(contentType)
            .OrderBy(d => myAssemblyName != null
                && d.GetType().Namespace?.Contains(myAssemblyName) == true ? 0 : 1)
            .FirstOrDefault();

        FileContent? fileContent = null;
        if (bestDecoder != null)
        {
            fileContent = await bestDecoder.DecodeAsync(binaryData, cancellationToken);
        }

        // Fallback: original content if nothing could decode
        return fileContent != null
            ? fileContent.GetFileItemFromFileContent(uri)
            : binaryData.ToFileItem(uri, mimeType: contentType, filename);
    }
}
