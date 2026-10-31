using System.Net;
using System.Net.Sockets;
using MCPhappey.Common.Models;

namespace MCPhappey.Core.Services;

/// <summary>Credential-free, DNS-pinned image downloads. Never uses configured scrapers, cookies, proxies, or caller headers.</summary>
public sealed class PublicImageDownloader
{
    public const int MaxImageBytes = 10 * 1024 * 1024;

    public async Task<FileItem> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = ConnectPublicAsync
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
                var location = response.Headers.Location ?? throw new InvalidOperationException("Image redirect has no location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            var mimeType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mimeType is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif" or "image/bmp"))
                throw new InvalidOperationException("The public URL must return a PNG, JPEG, WebP, GIF, or BMP image.");
            if (response.Content.Headers.ContentLength > MaxImageBytes)
                throw new InvalidOperationException("The input image exceeds the 10 MiB download limit.");
            using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > MaxImageBytes)
                    throw new InvalidOperationException("The input image exceeds the 10 MiB download limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            var bytes = output.ToArray();
            ValidateImage(bytes, mimeType);
            var extension = mimeType switch { "image/jpeg" => "jpg", "image/webp" => "webp", "image/gif" => "gif", "image/bmp" => "bmp", _ => "png" };
            return new FileItem { Filename = $"input.{extension}", MimeType = mimeType, Uri = uri.ToString(), Contents = BinaryData.FromBytes(bytes) };
        }
        throw new InvalidOperationException("Too many image redirects.");
    }

    public static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo))
            throw new UnauthorizedAccessException("Only public HTTPS image URLs without embedded credentials are supported.");
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IsProtectedMicrosoftHost(host)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && !IsPublicAddress(address)))
            throw new UnauthorizedAccessException("Private-network and protected Microsoft image URLs are unavailable without delegated access.");
    }

    public static bool IsProtectedMicrosoftHost(string host)
        => new[] { "graph.microsoft.com", "sharepoint.com", "sharepoint-df.com", "onedrive.live.com", "1drv.ms" }
            .Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (b[0] & 0xE0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x02)
                && !(b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0x0d && b[3] == 0xb8) || (b[2] == 0 && b[3] == 0)));
        return b[0] is not (0 or 10 or 127) && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2)))
            && !(b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100)))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113)
            && !address.Equals(IPAddress.Parse("168.63.129.16"));
    }

    public static void ValidateImage(ReadOnlySpan<byte> bytes, string mimeType)
    {
        var valid = mimeType switch
        {
            "image/png" => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.StartsWith(new byte[] { 255, 216, 255 }),
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            "image/gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            "image/bmp" => bytes.StartsWith("BM"u8),
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException("The returned data does not match its image content type.");
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new UnauthorizedAccessException("The image host resolves to a non-public network address.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        throw new HttpRequestException("The public image host could not be reached.");
    }
}
