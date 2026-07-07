using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Auth.Extensions;
using MCPhappey.Auth.Models;
using MCPhappey.Common;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SharePoint;

public static class SharePointREST
{

    [Description("Copy a raw SharePoint template file from a server-relative URL into a SharePoint document library folder. Useful for copying Content Type Hub template files into an Office template catalog library. Use exactly once per source template and target file unless intentionally overwriting.")]
    [McpServerTool(
            Title = "Copy SharePoint template file to document library",
            Name = "sharepoint_copy_template_file_to_document_library",
            Destructive = true,
            OpenWorld = false)]
                public static async Task<CallToolResult?> SharePointRest_CopyTemplateFileToDocumentLibrary(
            IServiceProvider serviceProvider,
            RequestContext<CallToolRequestParams> requestContext,

            [Description("Exact SharePoint web URL that owns the source template file, e.g. https://contoso.sharepoint.com/sites/contentTypeHub.")]
            string? sourceSiteUrl = null,

            [Description("Server-relative URL of the source template file, e.g. /sites/contentTypeHub/_cts/Projectdocument/template.dotx.")]
            string? sourceFileServerRelativeUrl = null,

            [Description("Exact SharePoint site URL containing the target template document library, e.g. https://contoso.sharepoint.com/sites/templates.")]
            string? targetSiteUrl = null,

            [Description("Server-relative URL of the target folder inside the template document library, e.g. /sites/templates/Office Templates or /sites/templates/Office Templates/Word.")]
            string? targetFolderServerRelativeUrl = null,

            [Description("Target file name including extension, e.g. Projectdocument.dotx. Defaults to the source file name.")]
            string? targetFileName = null,

            [Description("Overwrite the target file if it already exists. Default true.")]
            bool overwrite = true,

            [Description("Optional SharePoint list item metadata to set after upload. Keys must be internal field names. Values must be simple JSON values.")]
            Dictionary<string, JsonElement>? metadata = null,

            CancellationToken cancellationToken = default)
      => await requestContext.WithExceptionCheck(async () =>
      await requestContext.WithStructuredContent(async () =>
      {
          var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
              new SharePointCopyTemplateFileToDocumentLibraryInput
              {
                  SourceSiteUrl = sourceSiteUrl,
                  SourceFileServerRelativeUrl = sourceFileServerRelativeUrl,
                  TargetSiteUrl = targetSiteUrl,
                  TargetFolderServerRelativeUrl = targetFolderServerRelativeUrl,
                  TargetFileName = targetFileName,
                  Overwrite = overwrite,
                  Metadata = metadata
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

          var finalFileName = !string.IsNullOrWhiteSpace(typed.TargetFileName)
              ? typed.TargetFileName.Trim()
              : GetFileNameFromServerRelativeUrl(typed.SourceFileServerRelativeUrl!);

          var bytes = await ReadFileAsync(
              sourceClient,
              typed.SourceSiteUrl!,
              typed.SourceFileServerRelativeUrl!,
              cancellationToken);

          var uploadedFile = await AddFileToFolderUsingPathAsync(
              targetClient,
              typed.TargetSiteUrl!,
              typed.TargetFolderServerRelativeUrl!,
              finalFileName,
              bytes,
              typed.Overwrite ?? true,
              cancellationToken);

          var targetFileServerRelativeUrl = CombineServerRelativePath(
              typed.TargetFolderServerRelativeUrl!,
              finalFileName);

          object? metadataUpdateResult = null;

          if (typed.Metadata is { Count: > 0 })
          {
              metadataUpdateResult = await MergeFileListItemMetadataAsync(
                  targetClient,
                  typed.TargetSiteUrl!,
                  targetFileServerRelativeUrl,
                  typed.Metadata,
                  cancellationToken);
          }

          return new
          {
              typed.SourceSiteUrl,
              typed.SourceFileServerRelativeUrl,
              typed.TargetSiteUrl,
              typed.TargetFolderServerRelativeUrl,
              TargetFileName = finalFileName,
              TargetFileServerRelativeUrl = targetFileServerRelativeUrl,
              BytesCopied = bytes.Length,
              Overwrite = typed.Overwrite ?? true,
              MetadataUpdated = typed.Metadata is { Count: > 0 },
              MetadataUpdateResult = metadataUpdateResult,
              UploadedFile = uploadedFile,
              Status = "Copied template file to document library successfully."
          };
      }));

    [Description("Copy an existing SharePoint or OneDrive for Business file to a SharePoint list item as an attachment. Use exactly once per source file and target item. Do not repeat after a successful copy. For OneDrive files, sourceSiteUrl must be the full personal site URL, not the tenant root.")]
    [McpServerTool(
      Title = "Copy SharePoint file to list item attachment",
      Name = "sharepoint_copy_file_to_list_item_attachment",
      Destructive = true,
      OpenWorld = false)]
    public static async Task<CallToolResult?> SharePointRest_CopyFileToListItemAttachment(
      IServiceProvider serviceProvider,
      RequestContext<CallToolRequestParams> requestContext,

      [Description("Exact SharePoint web URL that owns the source file. For OneDrive, include the full /personal/... path, e.g. https://contoso-my.sharepoint.com/personal/user_contoso_com. Never use only https://contoso-my.sharepoint.com for OneDrive files.")]
    string? sourceSiteUrl = null,

      [Description("Server-relative URL of the source file. It must belong to sourceSiteUrl. For OneDrive this usually starts with /personal/user_contoso_com/..., e.g. /personal/user_contoso_com/Documents/file.pdf.")]
    string? sourceFileServerRelativeUrl = null,

      [Description("Exact SharePoint site URL containing the target list, e.g. https://contoso.sharepoint.com/sites/finance.")]
    string? targetSiteUrl = null,

      [Description("Exact title of the target SharePoint list.")]
    string? targetListTitle = null,

      [Description("Existing target SharePoint list item ID. Copy only once to this item after it succeeds.")]
    int? targetItemId = null,

      [Description("Optional attachment file name to use on the list item. Defaults to the source file name. Must not be reused for the same item unless intentionally replacing/duplicating.")]
    string? attachmentFileName = null,

      [Description("Recycle the source file only after the attachment copy succeeds. Default false. Use true only when the user explicitly wants the source removed afterwards.")]
    bool deleteSourceAfterCopy = false,

      CancellationToken cancellationToken = default)
          => await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
          {
              if (!string.IsNullOrWhiteSpace(sourceSiteUrl)
            && !string.IsNullOrWhiteSpace(sourceFileServerRelativeUrl)
            && sourceSiteUrl.Contains("-my.sharepoint.com", StringComparison.OrdinalIgnoreCase)
            && sourceFileServerRelativeUrl.StartsWith("/personal/", StringComparison.OrdinalIgnoreCase)
            && !new Uri(sourceSiteUrl).AbsolutePath.StartsWith("/personal/", StringComparison.OrdinalIgnoreCase))
              {
                  throw new InvalidOperationException(
                      "Invalid OneDrive sourceSiteUrl. For files under /personal/..., sourceSiteUrl must include the full personal site path, e.g. https://tenant-my.sharepoint.com/personal/user_domain_com. Do not use the tenant root.");
              }

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

    private static async Task<JsonElement?> AddFileToFolderUsingPathAsync(
    HttpClient client,
    string siteUrl,
    string targetFolderServerRelativeUrl,
    string fileName,
    byte[] bytes,
    bool overwrite,
    CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}/_api/web/GetFolderByServerRelativePath(decodedUrl=@folder)/Files/AddUsingPath(decodedUrl=@file,overwrite={overwrite.ToString().ToLowerInvariant()})" +
            $"?@folder={ODataQuoted(targetFolderServerRelativeUrl)}&@file={ODataQuoted(fileName)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement?> MergeFileListItemMetadataAsync(
        HttpClient client,
        string siteUrl,
        string fileServerRelativeUrl,
        Dictionary<string, JsonElement> metadata,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}/_api/web/GetFileByServerRelativePath(decodedUrl=@file)/ListItemAllFields" +
            $"?@file={ODataQuoted(fileServerRelativeUrl)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("IF-MATCH", "*");
        request.Headers.TryAddWithoutValidation("X-HTTP-Method", "MERGE");

        var cleanMetadata = new Dictionary<string, object?>();

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            cleanMetadata[pair.Key] = ConvertJsonElement(pair.Value);
        }

        request.Content = new StringContent(
            JsonSerializer.Serialize(cleanMetadata),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => JsonSerializer.Deserialize<object>(element.GetRawText())
        };
    }

    private static string CombineServerRelativePath(string folderServerRelativeUrl, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderServerRelativeUrl))
            throw new ValidationException("targetFolderServerRelativeUrl is required.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ValidationException("fileName is required.");

        return $"{folderServerRelativeUrl.TrimEnd('/')}/{fileName.TrimStart('/')}";
    }

    [Description("Confirm copying a raw SharePoint template file into a SharePoint document library folder.")]
    public class SharePointCopyTemplateFileToDocumentLibraryInput
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

        [JsonPropertyName("targetFolderServerRelativeUrl")]
        [Required]
        public string? TargetFolderServerRelativeUrl { get; set; }

        [JsonPropertyName("targetFileName")]
        public string? TargetFileName { get; set; }

        [JsonPropertyName("overwrite")]
        public bool? Overwrite { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, JsonElement>? Metadata { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(SourceSiteUrl, UriKind.Absolute, out _))
                throw new ValidationException("sourceSiteUrl must be absolute.");

            if (!Uri.TryCreate(TargetSiteUrl, UriKind.Absolute, out _))
                throw new ValidationException("targetSiteUrl must be absolute.");

            if (string.IsNullOrWhiteSpace(SourceFileServerRelativeUrl) ||
                !SourceFileServerRelativeUrl.StartsWith('/'))
            {
                throw new ValidationException("sourceFileServerRelativeUrl must start with '/'.");
            }

            if (string.IsNullOrWhiteSpace(TargetFolderServerRelativeUrl) ||
                !TargetFolderServerRelativeUrl.StartsWith('/'))
            {
                throw new ValidationException("targetFolderServerRelativeUrl must start with '/'.");
            }

            if (!string.IsNullOrWhiteSpace(TargetFileName) &&
                TargetFileName.Contains('/', StringComparison.Ordinal))
            {
                throw new ValidationException("targetFileName must be a file name only, not a path.");
            }

            if (Metadata != null)
            {
                foreach (var key in Metadata.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        throw new ValidationException("metadata contains an empty field name.");

                    if (key.Contains(' ', StringComparison.Ordinal))
                        throw new ValidationException($"metadata field '{key}' looks invalid. Use SharePoint internal field names.");
                }
            }
        }
    }

}