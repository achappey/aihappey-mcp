using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

public static partial class SharePointREST
{
    [Description(
        "Copy a SharePoint .spcolor theme file from an absolute URL to the target site's Site Assets library and apply it to the target site. Use exactly once per target site unless intentionally changing or reapplying the theme.")]
    [McpServerTool(
        Title = "Apply SharePoint theme from URL",
        Name = "sharepoint_apply_theme_from_url",
        Destructive = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> SharePointRest_ApplyThemeFromUrl(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,

        [Description(
        "Absolute URL of the source SharePoint .spcolor file, e.g. https://contoso.sharepoint.com/sites/branding/SiteAssets/Corporate.spcolor.")]
    string? themeUrl = null,

        [Description(
        "Exact SharePoint web URL to which the theme must be applied, e.g. https://contoso.sharepoint.com/sites/project.")]
    string? targetSiteUrl = null,

        [Description(
        "Overwrite the theme file in the target site's Site Assets library if it already exists. Default true.")]
    bool overwrite = true,

        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                new SharePointApplyThemeFromUrlInput
                {
                    ThemeUrl = themeUrl,
                    TargetSiteUrl = targetSiteUrl,
                    Overwrite = overwrite
                },
                cancellationToken);

            if (notAccepted != null)
                throw new Exception(JsonSerializer.Serialize(notAccepted));

            typed!.Validate();

            var tokenService = serviceProvider.GetRequiredService<HeaderProvider>();

            if (string.IsNullOrWhiteSpace(tokenService.Bearer))
                throw new UnauthorizedAccessException("Missing bearer token.");

            var httpClientFactory =
                serviceProvider.GetRequiredService<IHttpClientFactory>();

            var oauthSettings =
                serviceProvider.GetRequiredService<OAuthSettings>();

            var serverConfig =
                serviceProvider.GetServerConfig(requestContext.Server);

            var themeUri = new Uri(typed.ThemeUrl!);
            var targetUri = new Uri(typed.TargetSiteUrl!);

            var sourceSiteUrl = typed.ThemeUrl!.ExtractSiteUrl();

            var sourceFileServerRelativeUrl =
                Uri.UnescapeDataString(themeUri.AbsolutePath);

            using var sourceClient = await httpClientFactory.GetOboHttpClient(
                tokenService.Bearer,
                themeUri.Host,
                serverConfig!.Server,
                oauthSettings);

            using var targetClient = await httpClientFactory.GetOboHttpClient(
                tokenService.Bearer,
                targetUri.Host,
                serverConfig.Server,
                oauthSettings);

            var fileName = GetFileNameFromServerRelativeUrl(
                sourceFileServerRelativeUrl);

            var bytes = await ReadFileAsync(
                sourceClient,
                sourceSiteUrl,
                sourceFileServerRelativeUrl,
                cancellationToken);

            var siteAssetsFolderServerRelativeUrl =
                await EnsureSiteAssetsFolderAsync(
                    targetClient,
                    typed.TargetSiteUrl!,
                    cancellationToken);

            var uploadedFile = await AddFileToFolderUsingPathAsync(
                targetClient,
                typed.TargetSiteUrl!,
                siteAssetsFolderServerRelativeUrl,
                fileName,
                bytes,
                typed.Overwrite ?? true,
                cancellationToken);

            var targetThemeFileServerRelativeUrl =
                CombineServerRelativePath(
                    siteAssetsFolderServerRelativeUrl,
                    fileName);

            var applyThemeResponse = await ApplyThemeFileAsync(
                targetClient,
                typed.TargetSiteUrl!,
                targetThemeFileServerRelativeUrl,
                cancellationToken);

            return new
            {
                SourceSiteUrl = sourceSiteUrl,
                SourceThemeUrl = typed.ThemeUrl,
                SourceFileServerRelativeUrl = sourceFileServerRelativeUrl,

                TargetSiteUrl = typed.TargetSiteUrl,
                SiteAssetsFolderServerRelativeUrl =
                    siteAssetsFolderServerRelativeUrl,

                TargetThemeFileServerRelativeUrl =
                    targetThemeFileServerRelativeUrl,

                ThemeFileName = fileName,
                BytesCopied = bytes.Length,
                Overwrite = typed.Overwrite ?? true,
                ShareGenerated = false,

                UploadedFile = uploadedFile,
                ApplyThemeResponse = applyThemeResponse,

                Status = "Copied and applied SharePoint theme successfully."
            };
        }));

    private static async Task<string> EnsureSiteAssetsFolderAsync(
        HttpClient client,
        string siteUrl,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/lists/ensureSiteAssetsLibrary()";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        request.Content = new ByteArrayContent([]);

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = JsonDocument.Parse(json);

            var root = UnwrapSharePointResponse(
                document.RootElement);

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("RootFolder", out var rootFolder) &&
                rootFolder.ValueKind == JsonValueKind.Object &&
                rootFolder.TryGetProperty(
                    "ServerRelativeUrl",
                    out var folderUrlElement) &&
                !string.IsNullOrWhiteSpace(folderUrlElement.GetString()))
            {
                return folderUrlElement.GetString()!;
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idElement.GetString(), out var listId))
            {
                return await GetListRootFolderServerRelativeUrlAsync(
                    client,
                    siteUrl,
                    listId,
                    cancellationToken);
            }
        }

        // Defensive fallback. EnsureSiteAssetsLibrary creates SiteAssets
        // underneath the current web.
        var siteUri = new Uri(siteUrl);

        var siteServerRelativeUrl =
            Uri.UnescapeDataString(siteUri.AbsolutePath)
                .TrimEnd('/');

        return $"{siteServerRelativeUrl}/SiteAssets";
    }

    private static async Task<string>
        GetListRootFolderServerRelativeUrlAsync(
            HttpClient client,
            string siteUrl,
            Guid listId,
            CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            $"/_api/web/lists(guid'{listId:D}')/RootFolder" +
            "?$select=ServerRelativeUrl";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);

        using var document = JsonDocument.Parse(json);

        var root = UnwrapSharePointResponse(
            document.RootElement);

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(
                "ServerRelativeUrl",
                out var urlElement) ||
            string.IsNullOrWhiteSpace(urlElement.GetString()))
        {
            throw new InvalidOperationException(
                "SharePoint returned no server-relative URL " +
                "for the Site Assets root folder.");
        }

        return urlElement.GetString()!;
    }

    private static async Task<string?> ApplyThemeFileAsync(
        HttpClient client,
        string siteUrl,
        string colorPaletteServerRelativeUrl,
        CancellationToken cancellationToken)
    {
        var url =
            $"{siteUrl.TrimEnd('/')}" +
            "/_api/web/applyTheme(" +
            "colorPaletteUrl=@palette," +
            "fontSchemeUrl=null," +
            "backgroundImageUrl=null," +
            "shareGenerated=false)" +
            $"?@palette={ODataQuoted(colorPaletteServerRelativeUrl)}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        request.Content = new ByteArrayContent([]);

        using var response = await client.SendAsync(
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);

        return string.IsNullOrWhiteSpace(content)
            ? null
            : content;
    }

    private static JsonElement UnwrapSharePointResponse(
        JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("d", out var data))
        {
            return data;
        }

        return root;
    }

    [Description(
        "Confirm copying and applying a SharePoint .spcolor theme file to a target site.")]
    public class SharePointApplyThemeFromUrlInput
    {
        [JsonPropertyName("themeUrl")]
        [Required]
        public string? ThemeUrl { get; set; }

        [JsonPropertyName("targetSiteUrl")]
        [Required]
        public string? TargetSiteUrl { get; set; }

        [JsonPropertyName("overwrite")]
        public bool? Overwrite { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(
                    ThemeUrl,
                    UriKind.Absolute,
                    out var themeUri) ||
                themeUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "themeUrl must be an absolute HTTPS URL.");
            }

            if (!themeUri.AbsolutePath.EndsWith(
                    ".spcolor",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "themeUrl must reference a .spcolor file.");
            }

            if (!Uri.TryCreate(
                    TargetSiteUrl,
                    UriKind.Absolute,
                    out var targetUri) ||
                targetUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "targetSiteUrl must be an absolute HTTPS URL.");
            }

            if (!themeUri.Host.Contains(
                    "sharepoint",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "themeUrl must reference a SharePoint host.");
            }

            if (!targetUri.Host.Contains(
                    "sharepoint",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "targetSiteUrl must reference a SharePoint host.");
            }
        }
    }

    private static string ExtractSiteUrl(this string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException(
                "URL must be absolute.",
                nameof(url));

        var segments = uri.AbsolutePath
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var contentIndex = Array.FindIndex(
            segments,
            segment =>
                segment.Equals(
                    "SiteAssets",
                    StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(
                    "Shared Documents",
                    StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(
                    "Gedeelde documenten",
                    StringComparison.OrdinalIgnoreCase) ||
                segment.Equals(
                    "_catalogs",
                    StringComparison.OrdinalIgnoreCase));

        string sitePath;

        if (contentIndex >= 0)
        {
            sitePath = contentIndex == 0
                ? string.Empty
                : "/" + string.Join(
                    '/',
                    segments[..contentIndex]);
        }
        else if (
            segments.Length >= 2 &&
            (segments[0].Equals(
                 "sites",
                 StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals(
                 "teams",
                 StringComparison.OrdinalIgnoreCase) ||
             segments[0].Equals(
                 "personal",
                 StringComparison.OrdinalIgnoreCase)))
        {
            sitePath = $"/{segments[0]}/{segments[1]}";
        }
        else
        {
            sitePath = string.Empty;
        }

        return new UriBuilder(
            uri.Scheme,
            uri.Host,
            uri.IsDefaultPort ? -1 : uri.Port,
            sitePath)
            .Uri
            .AbsoluteUri
            .TrimEnd('/');
    }


    [Description(
        "Confirm copying and applying a SharePoint image as the target site's logo.")]
    public class SharePointApplySiteLogoFromUrlInput
    {
        [JsonPropertyName("logoUrl")]
        [Required]
        public string? LogoUrl { get; set; }

        [JsonPropertyName("targetSiteUrl")]
        [Required]
        public string? TargetSiteUrl { get; set; }

        [JsonPropertyName("targetFileName")]
        public string? TargetFileName { get; set; }

        [JsonPropertyName("overwrite")]
        public bool? Overwrite { get; set; }

        public void Validate()
        {
            if (!Uri.TryCreate(
                    LogoUrl,
                    UriKind.Absolute,
                    out var logoUri) ||
                logoUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "logoUrl must be an absolute HTTPS URL.");
            }

            if (!Uri.TryCreate(
                    TargetSiteUrl,
                    UriKind.Absolute,
                    out var targetUri) ||
                targetUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ValidationException(
                    "targetSiteUrl must be an absolute HTTPS URL.");
            }

            if (!logoUri.Host.EndsWith(
                    ".sharepoint.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "logoUrl must reference a SharePoint Online host.");
            }

            if (!targetUri.Host.EndsWith(
                    ".sharepoint.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "targetSiteUrl must reference a SharePoint Online host.");
            }

            if (!string.IsNullOrWhiteSpace(TargetFileName))
            {
                if (TargetFileName.Contains(
                        '/',
                        StringComparison.Ordinal) ||
                    TargetFileName.Contains(
                        '\\',
                        StringComparison.Ordinal))
                {
                    throw new ValidationException(
                        "targetFileName must be a file name only, not a path.");
                }

                if (string.IsNullOrWhiteSpace(
                        Path.GetExtension(TargetFileName)))
                {
                    throw new ValidationException(
                        "targetFileName must include a file extension.");
                }
            }

            var sourceFileName =
                Path.GetFileName(
                    Uri.UnescapeDataString(logoUri.AbsolutePath));

            if (string.IsNullOrWhiteSpace(sourceFileName))
            {
                throw new ValidationException(
                    "logoUrl must contain a file name.");
            }
        }
    }
}