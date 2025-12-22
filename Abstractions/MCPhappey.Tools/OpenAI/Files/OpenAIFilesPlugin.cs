using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenAI;
using OpenAI.Images;

namespace MCPhappey.Tools.OpenAI.Image;

public static class OpenAIFilesPlugin
{
    [Description("Download a file from OpenAI and uploads it in users' OneDrive")]
    [McpServerTool(Title = "Download OpenAI file", Destructive = false)]
    public static async Task<CallToolResult?> OpenAIFiles_DownloadFile(
     [Description("Id of the OpenAI file.")] string fileId,
     IServiceProvider serviceProvider,
     RequestContext<CallToolRequestParams> requestContext,
     CancellationToken cancellationToken = default) =>
      await requestContext.WithExceptionCheck(async () =>
    {
        var openAiClient = serviceProvider.GetRequiredService<OpenAIClient>();

        var client = openAiClient.GetOpenAIFileClient();
        var file = await client.DownloadFileAsync(fileId, cancellationToken);
        var fileItem = await client.GetFileAsync(fileId, cancellationToken);

        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            fileItem.Value.Filename,
            file.Value,
            cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    });
}

