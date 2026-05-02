using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointREST
{
    [Description("Copy a SharePoint/OneDrive for Business file to a SharePoint list item as an attachment. Optionally recycle the source file afterwards.")]
    [McpServerTool(
        Title = "Copy SharePoint file to list item attachment",
        Name = "sharepoint_copy_file_to_list_item_attachment",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SharePointRest_CopyFileToListItemAttachment(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Source site URL, e.g. https://contoso.sharepoint.com/sites/project or https://contoso-my.sharepoint.com/personal/user_contoso_com")] string? sourceSiteUrl = null,
        [Description("Source file server-relative URL, e.g. /sites/project/Shared Documents/file.pdf")] string? sourceFileServerRelativeUrl = null,
        [Description("Target site URL where the SharePoint list exists.")] string? targetSiteUrl = null,
        [Description("Target SharePoint list title.")] string? targetListTitle = null,
        [Description("Target SharePoint list item ID.")] int? targetItemId = null,
        [Description("Optional attachment file name. Defaults to source file name.")] string? attachmentFileName = null,
        [Description("Recycle source file after successful copy.")] bool deleteSourceAfterCopy = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SharePointCopyFileToListItemAttachmentInput
                {
                    SourceSiteUrl = sourceSiteUrl,
                    SourceFileServerRelativeUrl = sourceFileServerRelativeUrl,
                    TargetSiteUrl = targetSiteUrl,
                    TargetListTitle = targetListTitle,
                    TargetItemId = targetItemId,
                    AttachmentFileName = attachmentFileName,
                    DeleteSourceAfterCopy = deleteSourceAfterCopy
                },
                cancellationToken);

            if (notAccepted != null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            typed!.Validate();

            var tokenService = serviceProvider.GetRequiredService<HeaderProvider>();
            if (string.IsNullOrWhiteSpace(tokenService.Bearer))
                throw new UnauthorizedAccessException("Missing bearer token.");

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var oauthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
            var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);

            var sourceUri = new Uri(typed.SourceSiteUrl!);
            var targetUri = new Uri(typed.TargetSiteUrl!);

            using var sourceClient = await httpClientFactory.GetOboHttpClient(
                tokenService.Bearer,
                sourceUri.Host,
                serverConfig!.Server,
                oauthSettings);

            using var targetClient = await httpClientFactory.GetOboHttpClient(
                tokenService.Bearer,
                targetUri.Host,
                serverConfig.Server,
                oauthSettings);

            var fileName = !string.IsNullOrWhiteSpace(typed.AttachmentFileName)
                ? typed.AttachmentFileName.Trim()
                : GetFileNameFromServerRelativeUrl(typed.SourceFileServerRelativeUrl!);

            var bytes = await ReadFileAsync(
                sourceClient,
                typed.SourceSiteUrl!,
                typed.SourceFileServerRelativeUrl!,
                cancellationToken);

            await AddAttachmentAsync(
                targetClient,
                typed.TargetSiteUrl!,
                typed.TargetListTitle!,
                typed.TargetItemId!.Value,
                fileName,
                bytes,
                cancellationToken);

            string? recycleResponse = null;

            if (typed.DeleteSourceAfterCopy == true)
            {
                recycleResponse = await RecycleFileAsync(
                    sourceClient,
                    typed.SourceSiteUrl!,
                    typed.SourceFileServerRelativeUrl!,
                    cancellationToken);
            }

            return new
            {
                typed.SourceSiteUrl,
                typed.SourceFileServerRelativeUrl,
                typed.TargetSiteUrl,
                typed.TargetListTitle,
                typed.TargetItemId,
                AttachmentFileName = fileName,
                BytesCopied = bytes.Length,
                SourceRecycled = typed.DeleteSourceAfterCopy == true,
                RecycleResponse = recycleResponse,
                Status = "Copied file to list item attachment successfully."
            };
        }));

    private static async Task<byte[]> ReadFileAsync(
        HttpClient client,
        string siteUrl,
        string serverRelativeUrl,
        CancellationToken cancellationToken)
    {
        var url = $"{siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativePath(decodedUrl=@u)/$value?@u={ODataQuoted(serverRelativeUrl)}";

        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static async Task AddAttachmentAsync(
        HttpClient client,
        string siteUrl,
        string listTitle,
        int itemId,
        string fileName,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}/_api/web/lists/getbytitle(@list)/items({itemId})/AttachmentFiles/add(FileName=@file)?@list={ODataQuoted(listTitle)}&@file={ODataQuoted(fileName)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> RecycleFileAsync(
        HttpClient client,
        string siteUrl,
        string serverRelativeUrl,
        CancellationToken cancellationToken)
    {
        var url = $"{siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativePath(decodedUrl=@u)/recycle()?@u={ODataQuoted(serverRelativeUrl)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string ODataQuoted(string value)
    {
        var escaped = value.Replace("'", "''", StringComparison.Ordinal);
        return Uri.EscapeDataString($"'{escaped}'");
    }

    private static string GetFileNameFromServerRelativeUrl(string serverRelativeUrl)
    {
        var decoded = WebUtility.UrlDecode(serverRelativeUrl);
        var fileName = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ValidationException("Could not derive file name from sourceFileServerRelativeUrl.");

        return fileName;
    }

    [Description("Confirm copying a SharePoint/OneDrive for Business file to a SharePoint list item attachment.")]
    public class SharePointCopyFileToListItemAttachmentInput
    {
        [JsonPropertyName("sourceSiteUrl")]
        [Required]
        public string? SourceSiteUrl { get; set; }

        [JsonPropertyName("sourceFileServerRelativeUrl")]
        [Required]
        public string? SourceFileServerRelativeUrl { get; set; }

        [JsonPropertyName("targetSiteUrl")]
        [Required]
        public string? TargetSiteUrl { get; set; }

        [JsonPropertyName("targetListTitle")]
        [Required]
        public string? TargetListTitle { get; set; }

        [JsonPropertyName("targetItemId")]
        [Required]
        public int? TargetItemId { get; set; }

        [JsonPropertyName("attachmentFileName")]
        public string? AttachmentFileName { get; set; }

        [JsonPropertyName("deleteSourceAfterCopy")]
        public bool? DeleteSourceAfterCopy { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(SourceSiteUrl, UriKind.Absolute, out _))
                throw new ValidationException("sourceSiteUrl must be absolute.");

            if (!Uri.TryCreate(TargetSiteUrl, UriKind.Absolute, out _))
                throw new ValidationException("targetSiteUrl must be absolute.");

            if (string.IsNullOrWhiteSpace(SourceFileServerRelativeUrl) || !SourceFileServerRelativeUrl.StartsWith('/'))
                throw new ValidationException("sourceFileServerRelativeUrl must start with '/'.");

            if (string.IsNullOrWhiteSpace(TargetListTitle))
                throw new ValidationException("targetListTitle is required.");

            if (TargetItemId is null or <= 0)
                throw new ValidationException("targetItemId must be positive.");
        }
    }
}