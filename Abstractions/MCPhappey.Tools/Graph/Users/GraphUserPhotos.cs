using System.ComponentModel;
using System.Net;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Extensions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Graph.Users;

public static class GraphUserPhotos
{
    [Description("Get the current user's profile photo or another user's photo by optional userId as image.")]
    [McpServerTool(Title = "Get Microsoft user photo",
        Name = "graph_userphoto_get_photo",
        OpenWorld = false,
        ReadOnly = true)]
    public static async Task<CallToolResult?> GraphUserPhoto_GetPhoto(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional Microsoft Entra user id or userPrincipalName to fetch another user's photo. Leave empty for current user.")]
        string? userId = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        {
            var mcpServer = requestContext.Server;
            var httpClient = await serviceProvider.GetGraphHttpClient(mcpServer);

            var escapedUserId = Uri.EscapeDataString(userId ?? string.Empty);
            var relativePath = string.IsNullOrWhiteSpace(userId)
                ? "me/photo/$value"
                : $"users/{escapedUserId}/photo/$value";

            using var response = await httpClient.GetAsync(relativePath, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var target = string.IsNullOrWhiteSpace(userId) ? "current user" : $"user '{userId}'";
                return new CallToolResult
                {
                    IsError = true,
                    Content = [$"No profile photo found for {target}.".ToTextContentBlock()]
                };
            }

            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var mimeType = response.Content.Headers.ContentType?.MediaType;

            return new CallToolResult
            {
                Content =
                [
                    new ImageContentBlock
                    {
                        Data = bytes,
                        MimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType
                    }
                ]
            };
        });

    [Description("Get a Microsoft user photo, add its base64 payload into an HTML file placeholder (e.g. {userPhotoBase64}), and update that same HTML file in OneDrive/SharePoint.")]
    [McpServerTool(Title = "Add Microsoft user photo into HTML template",
        Name = "graph_userphoto_add_photo_to_html",
        OpenWorld = false,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> GraphUserPhoto_AddPhotoToHtml(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("SharePoint/OneDrive URL of the HTML file that contains a placeholder like {userPhotoBase64}.")]
        string htmlFileUrl,
        [Description("Placeholder variable name to replace. You can pass either 'userPhotoBase64' or '{userPhotoBase64}'.")]
        string variableName,
        [Description("Optional Microsoft Entra user id or userPrincipalName to fetch another user's photo. Leave empty for current user.")]
        string? userId = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithOboGraphClient(async graphClient =>
        {
            var mcpServer = requestContext.Server;
            var httpClient = await serviceProvider.GetGraphHttpClient(mcpServer);
            var downloadService = serviceProvider.GetRequiredService<DownloadService>();

            var escapedUserId = Uri.EscapeDataString(userId ?? string.Empty);
            var relativePath = string.IsNullOrWhiteSpace(userId)
                ? "me/photo/$value"
                : $"users/{escapedUserId}/photo/$value";

            using var response = await httpClient.GetAsync(relativePath, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var target = string.IsNullOrWhiteSpace(userId) ? "current user" : $"user '{userId}'";
                return new CallToolResult
                {
                    IsError = true,
                    Content = [$"No profile photo found for {target}.".ToTextContentBlock()]
                };
            }

            response.EnsureSuccessStatusCode();

            var photoBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var photoBase64 = Convert.ToBase64String(photoBytes);

            var htmlFiles = await downloadService.DownloadContentAsync(
                serviceProvider,
                mcpServer,
                htmlFileUrl,
                cancellationToken);

            var htmlFile = htmlFiles.FirstOrDefault()
                ?? throw new FileNotFoundException($"No HTML content found at '{htmlFileUrl}'.");

            var html = htmlFile.Contents.ToString();
            var token = variableName.Trim();
            if (token.StartsWith("{") && token.EndsWith("}") && token.Length > 2)
                token = token[1..^1];

            var placeholder = $"{{{token}}}";
            var updatedHtml = html.Replace(placeholder, $"data:{response.Content.Headers.ContentType};base64,{photoBase64}", StringComparison.Ordinal);

            var updated = await graphClient.UploadBinaryDataAsync(
                htmlFileUrl,
                BinaryData.FromString(updatedHtml),
                cancellationToken)
                ?? throw new FileNotFoundException($"Failed to update HTML file at '{htmlFileUrl}'.");

            return updated.ToResourceLinkBlock(updated.Name!).ToCallToolResult();
        }));
}
