using MCPhappey.Common.Models;
using Microsoft.AspNetCore.StaticFiles;
using ModelContextProtocol.Protocol;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace MCPhappey.Common.Extensions;

public static class FileItemExtensions
{
    public static string ToDataUri(this FileItem item) => $"data:{item.MimeType};base64,{Convert.ToBase64String(item.Contents)}";

    public static async Task<string> ToStringValueAsync(this Stream stream) => (await BinaryData.FromStreamAsync(stream)).ToString();

    public static string ToStringValue(this Stream stream) => BinaryData.FromStream(stream).ToString();

    public static byte[] ToArray(this Stream stream) => BinaryData.FromStream(stream).ToArray();

    public static bool IsText(this FileItem item) => item.MimeType.IsTextMimeType();

    public static IEnumerable<FileItem> GetTextFiles(this IEnumerable<FileItem> items) => items.Where(a => a.IsText());

    public static bool IsTextMimeType(this string text) => text.StartsWith("text/")
        || text.Equals(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase)
        || text.Equals(MediaTypeNames.Application.ProblemJson)
        || (text.StartsWith("application/") && text.EndsWith("+json"))
        || (text.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && text.EndsWith("+xml"))
        || text.Equals(MediaTypeNames.Application.Xml);

    public static FileItem ToFileItem<T>(this T content, string uri, string? filename = null) => new()
    {
        Contents = BinaryData.FromObjectAsJson(content, JsonSerializerOptions.Web),
        MimeType = MediaTypeNames.Application.Json,
        Uri = uri,
        Filename = filename
    };

    public static FileItem ToFileItem(this BinaryData binaryData,
          string uri,
          string mimeType = MediaTypeNames.Text.Plain,
          string? filename = null)
              => new()
              {
                  Contents = binaryData,
                  MimeType = mimeType,
                  Filename = filename,
                  Uri = uri,
              };


    public static FileItem ToJsonFileItem(this string content, string uri)
      => content.ToFileItem(uri, MediaTypeNames.Application.Json);

    public static FileItem ToFileItem(this string content,
        string uri,
        string mimeType = MediaTypeNames.Text.Plain)
            => new()
            {
                //  Stream = BinaryData.FromString(content).ToStream(),
                Contents = BinaryData.FromString(content),
                MimeType = mimeType,
                Uri = uri,
            };

    public static async Task<FileItem> ToFileItem(this HttpResponseMessage httpResponseMessage, string uri,
        CancellationToken cancellationToken = default) => new()
        {
            Contents = BinaryData.FromBytes(await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken)),
            // Stream = await httpResponseMessage.Content.ReadAsStreamAsync(),
            MimeType = httpResponseMessage.Content.Headers.ContentType?.MediaType!,
            Uri = uri,
        };

    public static ReadResourceResult ToReadResourceResult(this FileItem fileItem)
            => new()
            {
                Contents =
                    [
                      fileItem.ToResourceContents()
                    ]
            };

    public static ReadResourceResult ToReadResourceResult(this IEnumerable<FileItem> fileItems)
          => new()
          {
              Contents = [.. fileItems.Select(a => a.ToResourceContents())]
          };

    public static IEnumerable<ContentBlock> ToContentBlocks(this IEnumerable<FileItem> fileItems)
              => fileItems.Select(a => new EmbeddedResourceBlock()
              {
                  Resource = a.ToResourceContents()
              });

    public static ResourceContents ToResourceContents(this FileItem fileItem)
        => fileItem.MimeType.IsTextMimeType() ? new TextResourceContents()
        {
            Text = Encoding.UTF8.GetString(fileItem.Contents.ToArray()),
            MimeType = fileItem.MimeType,
            Uri = fileItem.Uri,
        } : new BlobResourceContents()
        {
            Blob = fileItem.Contents.ToArray(),
            MimeType = fileItem.MimeType,
            Uri = fileItem.Uri,
        };

    private static readonly FileExtensionContentTypeProvider _provider = new();

    public static string ResolveMimeFromExtension(this string? pathOrExt)
    {
        if (string.IsNullOrWhiteSpace(pathOrExt))
            return MediaTypeNames.Application.Octet;

        if (!_provider.TryGetContentType(pathOrExt, out var mime))
            mime = MediaTypeNames.Application.Octet;

        return mime;
    }

}

