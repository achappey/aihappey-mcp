using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.DumplingAI;

internal static class DumplingAIHelpers
{
    public static void EnsureExclusiveFileInput(string? fileUrl, string? inputFileBase64, string purpose)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(fileUrl);
        var hasBase64 = !string.IsNullOrWhiteSpace(inputFileBase64);

        if (hasUrl && hasBase64)
            throw new ValidationException($"Provide either fileUrl or inputFileBase64 for {purpose}, not both.");
    }

    public static async Task<JsonObject?> BuildOptionalFileObjectAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string? fileUrl,
        string? inputFileBase64,
        string? fileName,
        string? mimeType,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(inputFileBase64))
        {
            return new JsonObject
            {
                ["base64"] = inputFileBase64,
                ["filename"] = fileName,
                ["mimeType"] = mimeType
            }.WithoutNulls();
        }

        if (string.IsNullOrWhiteSpace(fileUrl))
            return null;

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var file = files.FirstOrDefault()
            ?? throw new Exception("No file found for DumplingAI fileUrl input.");

        return new JsonObject
        {
            ["base64"] = Convert.ToBase64String(file.Contents.ToArray()),
            ["filename"] = string.IsNullOrWhiteSpace(fileName) ? file.Filename : fileName,
            ["mimeType"] = string.IsNullOrWhiteSpace(mimeType) ? file.MimeType : mimeType,
            ["sourceFileUrl"] = fileUrl
        }.WithoutNulls();
    }

    public static async Task<string> BuildRequiredBase64FileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(
            serviceProvider,
            requestContext.Server,
            fileUrl,
            cancellationToken);

        var file = files.FirstOrDefault()
            ?? throw new Exception("No file found for DumplingAI fileUrl input.");

        return Convert.ToBase64String(file.Contents.ToArray());
    }

    public static async Task<string[]> BuildRequiredBase64FilesAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        IEnumerable<string> fileUrls,
        CancellationToken cancellationToken)
    {
        var normalized = fileUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .ToArray();

        if (normalized.Length == 0)
            throw new ValidationException("Provide at least one fileUrl.");

        var results = new List<string>(normalized.Length);
        foreach (var fileUrl in normalized)
        {
            results.Add(await BuildRequiredBase64FileAsync(
                serviceProvider,
                requestContext,
                fileUrl,
                cancellationToken));
        }

        return results.ToArray();
    }

    public static JsonObject CreateStructuredResponse(string endpoint, JsonNode? request, JsonNode? response)
        => new JsonObject
        {
            ["provider"] = "dumplingai",
            ["baseUrl"] = DumplingAIClient.BaseUrl.TrimEnd('/'),
            ["endpoint"] = endpoint,
            ["request"] = request?.DeepClone(),
            ["response"] = response?.DeepClone()
        }.WithoutNulls();

    public static JsonObject WithoutNulls(this JsonObject obj)
    {
        foreach (var property in obj.ToList())
        {
            if (property.Value is null)
            {
                obj.Remove(property.Key);
                continue;
            }

            if (property.Value is JsonObject child)
            {
                child.WithoutNulls();
                if (child.Count == 0)
                    obj.Remove(property.Key);
            }
        }

        return obj;
    }
}
