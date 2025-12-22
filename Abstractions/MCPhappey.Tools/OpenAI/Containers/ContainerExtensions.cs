using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using MCPhappey.Core.Extensions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Containers;

namespace MCPhappey.Tools.OpenAI.Containers;

public static class ContainerExtensions
{
    public const string BASE_URL = "https://api.openai.com/v1/containers";

    public static async Task<T> WithContainerClient<T>(
        this IServiceProvider serviceProvider, Func<ContainerClient, Task<T>> func)
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();

        return await func(openAiClient.GetContainerClient());
    }


    // ---- Public API ---------------------------------------------------------

    public static Task<ClientResult> UploadDataUriAsync(
        this ContainerClient containerClient,
        string containerId,
        string dataUri,
        string? explicitMimeType = null,
        string partName = "file",
        RequestOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentNullException(nameof(containerId));
        if (string.IsNullOrWhiteSpace(dataUri)) throw new ArgumentNullException(nameof(dataUri));

        // Split header/payload
        int comma = dataUri.IndexOf(',');
        if (comma < 0) throw new FormatException("Invalid data URI (no comma).");

        string header = dataUri[..comma];     // e.g. "data:application/pdf;base64"
        string payload = dataUri[(comma + 1)..];

        // Detect mime
        string mimeType = explicitMimeType ?? "application/octet-stream";
        const string prefix = "data:";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            int semi = header.IndexOf(';');
            if (semi > prefix.Length)
                mimeType = header[prefix.Length..semi];
        }

        // Decode
        byte[] bytes = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(payload)
            : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

        return UploadBytesMultipartAsync(containerClient, containerId, bytes, mimeType, partName, options);
    }

    public static async Task<ClientResult> UploadBytesMultipartAsync(
        dynamic containerClient,
        string containerId,
        byte[] data,
        string mimeType,
        string partName = "file",
        RequestOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(containerId)) throw new ArgumentNullException(nameof(containerId));
        if (data is null || data.Length == 0) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(mimeType)) mimeType = "application/octet-stream";

        // Filename: yyMMdd_HHmmss + extension resolved from MIME (built-in provider, no NuGet)
        string extension = mimeType.ResolveExtensionFromMime();
        string filename = $"{DateTime.UtcNow:yyMMdd_HHmmss}{extension}";

        // Build multipart body (single file part)
        string boundary = "----aihappey_" + Guid.NewGuid().ToString("N");
        byte[] body = BuildSingleFileMultipartBody(boundary, partName, filename, mimeType, data);

        // Wrap in BinaryContent and set the correct Content-Type with boundary
        var content = BinaryContent.Create(BinaryData.FromBytes(body));
        string contentType = $"multipart/form-data; boundary={boundary}";

        // You don’t need Content-Disposition on the request headers; multipart part has it.
        var req = options ?? new RequestOptions();

        return await containerClient.CreateContainerFileAsync(
            containerId,
            content,
            contentType,
            req
        );
    }

    // ---- Helpers ------------------------------------------------------------

    private static byte[] BuildSingleFileMultipartBody(
        string boundary,
        string partName,
        string filename,
        string fileMime,
        byte[] fileBytes)
    {
        // Multipart format:
        // --boundary\r\n
        // Content-Disposition: form-data; name="partName"; filename="filename"\r\n
        // Content-Type: fileMime\r\n
        // \r\n
        // <file bytes>\r\n
        // --boundary--\r\n

        var nl = "\r\n";
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.UTF8.GetBytes(s));
        void WriteBytes(byte[] b) => ms.Write(b, 0, b.Length);

        Write($"--{boundary}{nl}");
        Write($"Content-Disposition: form-data; name=\"{partName}\"; filename=\"{filename}\"{nl}");
        Write($"Content-Type: {fileMime}{nl}");
        Write(nl);
        WriteBytes(fileBytes);
        Write(nl);
        Write($"--{boundary}--{nl}");

        return ms.ToArray();
    }
}
