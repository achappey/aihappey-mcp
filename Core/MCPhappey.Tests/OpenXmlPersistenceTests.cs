using System.Net;
using System.Text;
using System.Text.Json;
using MCPhappey.Tools.Extensions;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace MCPhappey.Tests;

public sealed class OpenXmlPersistenceTests
{
    [Fact]
    public async Task Round_trip_uses_original_identity_and_version()
    {
        using var handler = new OfficeDrive();
        using var http = new HttpClient(handler);
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var snapshot = await graph.DownloadOfficeDocumentAsync("https://example.sharepoint.com/document");

        Assert.Equal("original", snapshot.Contents.ToString());
        await graph.UploadOfficeDocumentAsync(snapshot.Item, BinaryData.FromString("updated"));

        Assert.Equal("\"v1\"", handler.IfMatch);
        Assert.Equal("updated", handler.Content);
        Assert.Equal(1, handler.MetadataReads);
        Assert.Equal(1, handler.Writes);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(423)]
    public async Task Concurrent_edit_or_lock_does_not_overwrite_or_retry(int status)
    {
        using var handler = new OfficeDrive { WriteStatus = status };
        using var http = new HttpClient(handler);
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());
        var snapshot = await graph.DownloadOfficeDocumentAsync("https://example.sharepoint.com/document");

        await Assert.ThrowsAnyAsync<ApiException>(() =>
            graph.UploadOfficeDocumentAsync(snapshot.Item, BinaryData.FromString("updated")));

        Assert.Equal("original", handler.Content);
        Assert.Equal("\"v1\"", handler.IfMatch);
        Assert.Equal(1, handler.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_version_fails_before_download_or_write(string? etag)
    {
        using var handler = new OfficeDrive { ETag = etag };
        using var http = new HttpClient(handler);
        using var graph = new GraphServiceClient(http, new AnonymousAuthenticationProvider());

        await Assert.ThrowsAsync<IOException>(() =>
            graph.DownloadOfficeDocumentAsync("https://example.sharepoint.com/document"));
        await Assert.ThrowsAsync<IOException>(() => graph.UploadOfficeDocumentAsync(
            new DriveItem { Id = "item", ParentReference = new ItemReference { DriveId = "drive" } },
            BinaryData.FromString("updated")));

        Assert.Equal(0, handler.ContentReads);
        Assert.Equal(0, handler.Writes);
    }

    private sealed class OfficeDrive : HttpMessageHandler
    {
        public string? ETag { get; init; } = "\"v1\"";
        public int WriteStatus { get; init; } = 200;
        public int MetadataReads { get; private set; }
        public int ContentReads { get; private set; }
        public int Writes { get; private set; }
        public string? IfMatch { get; private set; }
        public string Content { get; private set; } = "original";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("/shares/", StringComparison.Ordinal))
            {
                MetadataReads++;
                return Json(new { id = "item", eTag = ETag, parentReference = new { driveId = "drive" } });
            }

            Assert.EndsWith("/drives/drive/items/item/content", path);
            if (request.Method == HttpMethod.Get)
            {
                ContentReads++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Content) };
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            Writes++;
            IfMatch = request.Headers.TryGetValues("If-Match", out var values) ? Assert.Single(values) : null;
            if (WriteStatus != 200)
                return Json(new { error = new { code = "conflict", message = "Document changed or locked." } }, WriteStatus);
            Assert.Equal(ETag, IfMatch);
            Content = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json(new { id = "item", eTag = "\"v2\"" });
        }

        private static HttpResponseMessage Json(object value, int status = 200)
            => new((HttpStatusCode)status)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
    }
}
