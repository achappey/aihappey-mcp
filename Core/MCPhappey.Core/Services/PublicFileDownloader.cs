using System.Net;
using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services;

/// <summary>Bounded public downloads without caller headers, operator credentials, cookies, or proxies.</summary>
public sealed class PublicFileDownloader
{
    public const int MaxFileBytes = 32 * 1024 * 1024;

    public async Task<FileItem> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = PublicImageDownloader.ConnectPublicAsync
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var uri = new Uri(url, UriKind.Absolute);
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            ValidateUri(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location ?? throw new InvalidOperationException("File redirect has no location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxFileBytes)
                throw new InvalidOperationException("The input file exceeds the 32 MiB download limit.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > MaxFileBytes)
                    throw new InvalidOperationException("The input file exceeds the 32 MiB download limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            return ToFileItem(response, uri, BinaryData.FromBytes(output.ToArray()));
        }
        throw new InvalidOperationException("Too many file redirects.");
    }

    public static void ValidateUri(Uri uri)
    {
        try
        {
            PublicImageDownloader.ValidateUri(uri);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                "Only public HTTPS file URLs are supported in header mode. Protected Microsoft files and folders require delegated authentication; private-network URLs are not allowed.", exception);
        }
    }

    public static FileItem ToFileItem(HttpResponseMessage response, Uri uri, BinaryData contents)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var suppliedName = disposition?.FileNameStar ?? disposition?.FileName;
        var filename = Path.GetFileName((suppliedName ?? Uri.UnescapeDataString(uri.AbsolutePath))
            .Trim('"').Replace('\\', '/'));
        return new FileItem
        {
            Filename = string.IsNullOrWhiteSpace(filename) ? "input" : filename,
            MimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
            Uri = uri.ToString(),
            Contents = contents
        };
    }
}
