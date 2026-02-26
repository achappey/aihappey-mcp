using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MCPhappey.Common.Extensions;

namespace MCPhappey.Tools.Mixedbread;

public static class MixedbreadFiles
{
    [Description("Upload a file to Mixedbread using fileUrl input.")]
    [McpServerTool(
        Title = "Mixedbread Upload File",
        Name = "mixedbread_files_upload",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadFiles_Upload(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File URL to upload (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadFileUploadRequest
                    {
                        FileUrl = fileUrl
                    },
                    cancellationToken);           

                using var form = new MultipartFormDataContent();
                var content = new StreamContent(file.Contents.ToStream());
                content.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/octet-stream");
                form.Add(content, "file", file.Filename ?? "upload.bin");

                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/files") { Content = form };
                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Update a file on Mixedbread using fileUrl input.")]
    [McpServerTool(
        Title = "Mixedbread Update File",
        Name = "mixedbread_files_update",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = false)]
    public static async Task<CallToolResult?> MixedbreadFiles_Update(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File ID to update.")] string fileId,
        [Description("File URL to upload as the new content (SharePoint/OneDrive/HTTPS supported).")]
        string fileUrl,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
                ArgumentException.ThrowIfNullOrWhiteSpace(fileUrl);

                var downloadService = serviceProvider.GetRequiredService<DownloadService>();
                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();

                var downloads = await downloadService.DownloadContentAsync(
                    serviceProvider,
                    requestContext.Server,
                    fileUrl,
                    cancellationToken);

                var file = downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException("Failed to download file from fileUrl.");

                var (typed, notAccepted, _) = await requestContext.Server.TryElicit(
                    new MixedbreadFileUpdateRequest
                    {
                        FileId = fileId,
                        FileUrl = fileUrl
                    },
                    cancellationToken);

                using var form = new MultipartFormDataContent();
                var content = new StreamContent(file.Contents.ToStream());
                content.Headers.ContentType = new MediaTypeHeaderValue(file.MimeType ?? "application/octet-stream");
                form.Add(content, "file", file.Filename ?? "upload.bin");

                using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/files/{Uri.EscapeDataString(fileId)}")
                {
                    Content = form
                };
                var json = await MixedbreadHttp.SendAsync(client, request, cancellationToken);
                return json;
            }));

    [Description("Delete a Mixedbread file by ID.")]
    [McpServerTool(
        Title = "Mixedbread Delete File",
        Name = "mixedbread_files_delete",
        OpenWorld = true,
        ReadOnly = false,
        Destructive = true)]
    public static async Task<CallToolResult?> MixedbreadFiles_Delete(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("File ID to delete.")] string fileId,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                if (string.IsNullOrWhiteSpace(fileId))
                    throw new ArgumentException("fileId is required.");

                var settings = serviceProvider.GetRequiredService<MixedbreadSettings>();

                return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteMixedbreadFile>(
                    fileId,
                    async ct =>
                    {
                        using var client = MixedbreadHttp.CreateClient(serviceProvider, settings);
                        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/files/{Uri.EscapeDataString(fileId)}");
                        _ = await MixedbreadHttp.SendAsync(client, request, ct);
                    },
                    $"File '{fileId}' deleted successfully.",
                    cancellationToken);
            }));
}

[Description("Please confirm the Mixedbread file upload.")]
public sealed class MixedbreadFileUploadRequest
{
    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("File URL to upload.")]
    public string FileUrl { get; set; } = default!;
}

[Description("Please confirm the Mixedbread file update.")]
public sealed class MixedbreadFileUpdateRequest
{
    [JsonPropertyName("fileId")]
    [Required]
    [Description("File ID to update.")]
    public string FileId { get; set; } = default!;

    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("File URL to upload.")]
    public string FileUrl { get; set; } = default!;
}

[Description("Please confirm deletion of the file ID: {0}")]
public sealed class ConfirmDeleteMixedbreadFile : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("The file ID to delete (must match exactly).")]
    public string Name { get; set; } = default!;
}
