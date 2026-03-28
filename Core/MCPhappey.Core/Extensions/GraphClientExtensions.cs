using System.Net.Mime;
using System.Collections.Concurrent;
using System.Text.Json;
using MCPhappey.Common.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;
using ModelContextProtocol.Server;
using MCPhappey.Auth.Models;
using MCPhappey.Auth.Extensions;
using MCPhappey.Common.Constants;
using ModelContextProtocol.Protocol;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Core.Extensions;

public static class GraphClientExtensions
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> _appTokenCache = new();

    public static async Task<GraphServiceClient> GetOboGraphClient(this IServiceProvider serviceProvider,
      McpServer mcpServer)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var oAuthSettings = serviceProvider.GetService<OAuthSettings>();
        var server = serviceProvider.GetServerConfig(mcpServer);

        return await httpClientFactory.GetOboGraphClient(tokenService?.Bearer!, server?.Server!, oAuthSettings!);
    }

    public static async Task<GraphServiceClient> GetAppGraphClient(this IServiceProvider serviceProvider)
    {
        var token = await serviceProvider.GetAppGraphToken();
        var authProvider = new StaticTokenAuthProvider(token);
        return new GraphServiceClient(authProvider);
    }

    public static async Task<string> GetAppGraphToken(this IServiceProvider serviceProvider)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();

        return await httpClientFactory.GetClientCredentialsToken(oAuthSettings, GraphDefaultScope);
    }

    public static async Task<string> GetOboGraphToken(this IServiceProvider serviceProvider,
      McpServer mcpServer)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var server = serviceProvider.GetServerConfig(mcpServer);
        var oAuthSettings = serviceProvider.GetService<OAuthSettings>();
        var delegated = await httpClientFactory.GetOboToken(tokenService?.Bearer!, Hosts.MicrosoftGraph, server?.Server!, oAuthSettings!);

        return delegated;
    }


    public static async Task<GraphServiceClient> GetOboGraphClient(this IHttpClientFactory httpClientFactory,
        string token,
        Server server,
        OAuthSettings oAuthSettings)
    {
        var delegated = await httpClientFactory.GetOboToken(token, Hosts.MicrosoftGraph, server, oAuthSettings);

        var authProvider = new StaticTokenAuthProvider(delegated!);
        return new GraphServiceClient(authProvider);
    }

    public static async Task<string> GetClientCredentialsToken(
        this IHttpClientFactory httpClientFactory,
        OAuthSettings oAuthSettings,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(oAuthSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(oAuthSettings.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oAuthSettings.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oAuthSettings.ClientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var cacheKey = $"{oAuthSettings.TenantId}::{oAuthSettings.ClientId}::{scope}";
        if (_appTokenCache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow < cached.ExpiresAt)
        {
            return cached.Token;
        }

        using var http = httpClientFactory.CreateClient();
        var tokenEndpoint = $"https://login.microsoftonline.com/{oAuthSettings.TenantId}/oauth2/v2.0/token";

        var form = new Dictionary<string, string>
        {
            ["client_id"] = oAuthSettings.ClientId,
            ["client_secret"] = oAuthSettings.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = scope
        };

        using var response = await http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form));
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"App token request failed: {payload}");

        using var json = JsonDocument.Parse(payload);
        var accessToken = json.RootElement.GetProperty("access_token").GetString();
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var exp)
            ? exp.GetInt32()
            : 3600;

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new UnauthorizedAccessException("App token response did not contain an access_token.");

        _appTokenCache[cacheKey] = (accessToken, DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
        return accessToken;
    }

    public static async Task<HttpClient> GetGraphHttpClient(this IServiceProvider serviceProvider, McpServer mcpServer)
    {
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var tokenService = serviceProvider.GetRequiredService<HeaderProvider>();
        var oAuthSettings = serviceProvider.GetRequiredService<OAuthSettings>();
        var server = serviceProvider.GetServerConfig(mcpServer);

        // Haal OBO-access token op zoals je nu doet
        var accessToken = await httpClientFactory.GetOboToken(
            tokenService.Bearer!,
            Hosts.MicrosoftGraph,
            server?.Server!,
            oAuthSettings
        );

        // Maak Graph client (default handler)
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        client.BaseAddress = new Uri("https://graph.microsoft.com/beta/");

        return client;
    }


    public static Task<DriveItem?> GetDriveItem(this GraphServiceClient client, string link,
           CancellationToken cancellationToken = default)
    {
        string base64Value = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(link));
        string encodedUrl = "u!" + base64Value.TrimEnd('=').Replace('/', '_').Replace('+', '-');

        return client.Shares[encodedUrl].DriveItem.GetAsync(cancellationToken: cancellationToken);
    }

    public static Resource ToResource(this DriveItem driveItem) =>
            new()
            {
                Name = driveItem?.Name!,
                Uri = driveItem?.WebUrl!,
                Size = driveItem?.Size,
                Description = driveItem?.Description,
                MimeType = driveItem?.File?.MimeType ?? (driveItem?.Folder != null
                    ? MediaTypeNames.Application.Json : driveItem?.File?.MimeType),
                Annotations = new Annotations()
                {
                    LastModified = driveItem?.LastModifiedDateTime
                }
            };

    public static ResourceLinkBlock ToResourceLinkBlock(this DriveItem driveItem, string filename)
            => driveItem.WebUrl!.ToResourceLinkBlock(driveItem?.Name ?? filename, driveItem?.File?.MimeType, driveItem?.Description, driveItem?.Size);


    public static async Task<ResourceLinkBlock?> Upload(this GraphServiceClient graphServiceClient,
              string filename,
              BinaryData binaryData,
              CancellationToken cancellationToken = default)
    {
        using var uploadStream = new MemoryStream(binaryData.ToArray());

        var myDrive = await graphServiceClient.Me.Drive.GetAsync(cancellationToken: cancellationToken);
        var uploadedItem = await graphServiceClient.Drives[myDrive?.Id].Root.ItemWithPath($"/{filename}")
            .Content.PutAsync(uploadStream, cancellationToken: cancellationToken);

        var retrievedItem = await graphServiceClient.Drives[myDrive?.Id].Items[uploadedItem?.Id]
            .GetAsync(cancellationToken: cancellationToken);

        return retrievedItem?.ToResourceLinkBlock(filename) ?? throw new Exception("Something went wrong");

    }

    public static async Task<ResourceLinkBlock?> UploadToFolder(this GraphServiceClient graphServiceClient,
              string folderUrl,
              string filename,
              BinaryData binaryData,
              CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);

        var folderItem = await graphServiceClient.GetDriveItem(folderUrl, cancellationToken)
            ?? throw new IOException($"Could not resolve folder URL '{folderUrl}'.");

        if (folderItem.Folder == null)
            throw new IOException($"The URL '{folderUrl}' does not point to a folder.");

        var driveId = folderItem.ParentReference?.DriveId
            ?? throw new IOException("DriveId missing for target folder.");
        var folderId = folderItem.Id
            ?? throw new IOException("FolderId missing for target folder.");

        using var uploadStream = new MemoryStream(binaryData.ToArray());

        var uploadedItem = await graphServiceClient.Drives[driveId].Items[folderId]
            .ItemWithPath(filename)
            .Content
            .PutAsync(uploadStream, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(uploadedItem?.Id))
            throw new IOException($"Upload failed for '{filename}'.");

        var retrievedItem = await graphServiceClient.Drives[driveId].Items[uploadedItem.Id]
            .GetAsync(cancellationToken: cancellationToken);

        return retrievedItem?.ToResourceLinkBlock(filename)
            ?? throw new IOException($"Uploaded item could not be retrieved for '{filename}'.");
    }

    public static async Task<(string UploadName, string FolderUrl)?> TryResolveSiblingOutputTargetAsync(
        this GraphServiceClient graphServiceClient,
        string sourceUrl,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphServiceClient);

        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        var sourceItem = await graphServiceClient.GetDriveItem(sourceUrl, cancellationToken);
        if (sourceItem == null || sourceItem.Folder != null || string.IsNullOrWhiteSpace(sourceItem.Name))
            return null;

        var driveId = sourceItem.ParentReference?.DriveId;
        var folderId = sourceItem.ParentReference?.Id;
        if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(folderId))
            return null;

        var folderItem = await graphServiceClient.Drives[driveId].Items[folderId]
            .GetAsync(cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(folderItem?.WebUrl))
            return null;

        return ($"{sourceItem.Name}.LLMs.{extension}", folderItem.WebUrl);
    }

}

