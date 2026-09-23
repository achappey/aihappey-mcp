using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AgentSandbox;

public static class AgentSandboxFiles
{
    [Description("Upload a file from an HTTPS, SharePoint, or OneDrive URL to AgentSandbox cloud storage, optionally injecting it into a running session.")]
    [McpServerTool(
        Title = "AgentSandbox upload file",
        Name = "agentsandbox_files_upload",
        ReadOnly = false,
        Destructive = false,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Upload(
        [Description("HTTPS, SharePoint, or OneDrive file URL to download and upload to AgentSandbox.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional running AgentSandbox session ID that receives the uploaded file in /workspace.")] string? sessionId = null,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent(async () =>
            {
                AgentSandboxHelpers.Require(fileUrl, nameof(fileUrl));
                var files = await serviceProvider
                    .GetRequiredService<DownloadService>()
                    .DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
                var file = files.FirstOrDefault()
                    ?? throw new ValidationException("The fileUrl content could not be downloaded.");

                var fileName = Path.GetFileName(file.Filename);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "upload.bin";

                using var form = new MultipartFormDataContent();
                var fileContent = new StreamContent(file.Contents.ToStream());
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    string.IsNullOrWhiteSpace(file.MimeType) ? "application/octet-stream" : file.MimeType);
                form.Add(fileContent, "file", fileName);

                var path = "v1/files";
                if (!string.IsNullOrWhiteSpace(sessionId))
                    path += $"?session_id={Uri.EscapeDataString(sessionId.Trim())}";

                var response = await serviceProvider
                    .GetRequiredService<AgentSandboxClient>()
                    .PostMultipartAsync(path, form, cancellationToken);

                return response.StructuredOrStatus("upload file");
            }));

    [Description("Permanently delete an owned AgentSandbox file and its cloud blob after explicit typed confirmation.")]
    [McpServerTool(
        Title = "AgentSandbox delete file",
        Name = "agentsandbox_files_delete",
        ReadOnly = false,
        Destructive = true,
        OpenWorld = true)]
    public static async Task<CallToolResult?> Delete(
        [Description("AgentSandbox file ID to delete.")] string fileId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () =>
        {
            var normalizedFileId = AgentSandboxHelpers.Require(fileId, nameof(fileId));
            var client = serviceProvider.GetRequiredService<AgentSandboxClient>();

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteAgentSandboxFile>(
                normalizedFileId,
                async ct => _ = await client.DeleteAsync(
                    $"v1/files/{AgentSandboxHelpers.EscapePath(normalizedFileId)}",
                    ct),
                $"AgentSandbox file '{normalizedFileId}' deleted successfully.",
                cancellationToken);
        });
}

[Description("Please confirm deletion of AgentSandbox file: {0}")]
public sealed class ConfirmDeleteAgentSandboxFile : IHasName
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Type the file ID exactly to confirm permanent deletion.")]
    public string Name { get; set; } = string.Empty;
}
