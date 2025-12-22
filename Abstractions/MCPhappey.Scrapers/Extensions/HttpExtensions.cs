
using System.Net.Mime;
using MCPhappey.Common.Models;

namespace MCPhappey.Scrapers.Extensions;

public static class HttpExtensions
{

    public static async Task<FileItem> ToFileItem(this HttpResponseMessage httpResponseMessage, string uri,
     CancellationToken cancellationToken = default) => new()
     {
         Contents = BinaryData.FromBytes(await httpResponseMessage.Content.ReadAsByteArrayAsync(cancellationToken)),
         MimeType = httpResponseMessage.Content.Headers.ContentType?.MediaType ?? MediaTypeNames.Application.Octet,
         Uri = uri,
     };

    public static async Task<HttpResponseMessage> GetWithContentExceptionAsync(
           this HttpClient httpClient,
           string url,
           CancellationToken cancellationToken = default)
    {
        var result = await httpClient.GetAsync(url, cancellationToken);

        if (!result.IsSuccessStatusCode)
        {
            var stringText = await result.Content.ReadAsStringAsync(cancellationToken);

            throw new Exception(stringText);
        }

        return result; // Caller disposes!
    }

}