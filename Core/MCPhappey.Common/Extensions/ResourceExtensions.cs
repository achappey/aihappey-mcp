using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace MCPhappey.Common.Extensions;

public static class ResourceExtensions
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static EmbeddedResourceBlock ToJsonContentBlock<T>(this T content, string uri)
            => JsonSerializer.Serialize(content, JsonSerializerOptions)
            .ToTextResourceContent(uri, MediaTypeNames.Application.Json);

    public static ReadResourceResult ToReadResourceResult(this string content,
        string uri,
        string mimeType = MediaTypeNames.Text.Plain)
        => new()
        {
            Contents =
                [
                    content.ToTextResourceContents(uri, mimeType)
                ]
        };

    public static ReadResourceResult ToJsonReadResourceResult(this string content, string uri)
        => content.ToReadResourceResult(uri, MediaTypeNames.Application.Json);

    public static TextResourceContents ToTextResourceContents(this string contents, string uri,
           string mimeType = MediaTypeNames.Text.Plain) => new()
           {
               Uri = uri,
               Text = contents,
               MimeType = mimeType
           };

    public static EmbeddedResourceBlock ToTextResourceContent(this string contents, string uri,
        string mimeType = MediaTypeNames.Text.Plain) => new()
        {
            Resource = contents.ToTextResourceContents(uri, mimeType)
        };

    public static ContentBlock ToJsonContent(this string contents, string uri) =>
        contents.ToTextResourceContent(uri, MediaTypeNames.Application.Json);

    public static ContentBlock ToJsonContent(this JsonElement contents, string uri) =>
        contents.GetRawText().ToJsonContent(uri);

    public static ContentBlock ToJsonContent(this JsonDocument contents, string uri) =>
        contents.RootElement.ToJsonContent(uri);

    public static ContentBlock ToJsonContent(this JsonObject contents, string uri) =>
        contents.ToJsonString().ToJsonContent(uri);

    public static ContentBlock ToBlobContent(this byte[] contents, string uri, string mimeType) =>
        new EmbeddedResourceBlock()
        {
            Resource = contents.ToBlobResourceContents(uri, mimeType)
        };

    public static ContentBlock ToBlobContent(this BinaryData binaryData, string uri, string mimeType) =>
        new EmbeddedResourceBlock()
        {
            Resource = binaryData.ToBlobResourceContents(uri, mimeType)
        };

    public static BlobResourceContents ToBlobResourceContents(this BinaryData binaryData, string uri, string mimeType) =>
        new()
        {
            Uri = uri,
            Blob = Convert.ToBase64String(binaryData),
            MimeType = mimeType
        };

    public static BlobResourceContents ToBlobResourceContents(this byte[] contents, string uri, string mimeType) =>
        new()
        {
            Uri = uri,
            Blob = Convert.ToBase64String(contents),
            MimeType = mimeType
        };

    public static EmbeddedResourceBlock ToContent(this ResourceContents contents) => new()
    {
        Resource = contents
    };
}
