using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.CaseDev;

internal static class CaseDevHelpers
{
    public static Dictionary<string, string>? GetHostHeaders(
        Dictionary<string, Dictionary<string, string>>? headers,
        params string[] hosts)
    {
        foreach (var host in hosts)
        {
            var match = headers?
                .FirstOrDefault(h => h.Key.Equals(host, StringComparison.OrdinalIgnoreCase))
                .Value;

            if (match is { Count: > 0 })
                return new Dictionary<string, string>(match, StringComparer.OrdinalIgnoreCase);
        }

        return null;
    }

    public static List<string> ParseDelimited(string? input)
        => string.IsNullOrWhiteSpace(input)
            ? []
            : input
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static JsonArray? ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            array.Add(value);

        return array.Count == 0 ? null : array;
    }

    public static JsonObject? ParseObject(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var parsed = JsonNode.Parse(json) as JsonObject;
        if (parsed == null)
            throw new ValidationException($"{parameterName} must be a valid JSON object string.");

        return parsed;
    }

    public static JsonArray ParseArray(string? json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException($"{parameterName} is required.");

        var parsed = JsonNode.Parse(json) as JsonArray;
        if (parsed == null)
            throw new ValidationException($"{parameterName} must be a valid JSON array string.");

        return parsed;
    }

    public static async Task<FileItem> DownloadSingleFileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            throw new ValidationException("fileUrl is required.");

        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloader.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        return files.FirstOrDefault() ?? throw new InvalidOperationException("Failed to download source file from fileUrl.");
    }

    public static async Task<string> ScrapeTextFromFileUrlAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloader = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloader.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        var text = string.Join("\n\n", files.Select(f => f.Contents.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No readable text content was extracted from fileUrl.");

        return text;
    }

    public static async Task<CallToolResult> ToUploadOrStructuredResultAsync(
        this CaseDevResponse response,
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        string filenameBase,
        string preferredExtension,
        CancellationToken cancellationToken)
    {
        if (response.Json != null)
        {
            return new CallToolResult
            {
                Meta = await requestContext.GetToolMeta(),
                StructuredContent = response.Json,
                Content = ["Case.dev returned JSON output.".ToTextContentBlock()]
            };
        }

        var normalizedExtension = NormalizeExtension(preferredExtension);
        var uploadName = $"{filenameBase}{normalizedExtension}";
        var uploaded = await requestContext.Server.Upload(
            serviceProvider,
            uploadName,
            BinaryData.FromBytes(response.Bytes),
            cancellationToken);

        if (uploaded == null)
            throw new InvalidOperationException("Case.dev output upload failed.");

        var structured = new JsonObject
        {
            ["type"] = "resource",
            ["mimeType"] = response.ContentType ?? uploadName.ResolveMimeFromExtension(),
            ["filename"] = uploadName,
            ["size"] = response.Bytes.Length,
            ["resourceLink"] = uploaded.Uri,
            ["name"] = uploaded.Name,
            ["description"] = uploaded.Description
        };

        return new CallToolResult
        {
            Meta = await requestContext.GetToolMeta(),
            StructuredContent = structured,
            Content = [uploaded]
        };
    }

    public static string BuildBaseFilename(RequestContext<CallToolRequestParams> requestContext, string? filename)
        => string.IsNullOrWhiteSpace(filename)
            ? requestContext.ToOutputFileName()
            : filename.ToOutputFileName();

    public static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return ".bin";

        return extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
    }
}

[Description("Please confirm deletion of the Case.dev agent id: {0}")]
internal sealed class ConfirmDeleteCaseDevAgent : IHasName
{
    public string Name { get; set; } = string.Empty;
}
