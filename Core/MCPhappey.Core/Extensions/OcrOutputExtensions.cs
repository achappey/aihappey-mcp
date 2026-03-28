using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class OcrOutputExtensions
{
    private static readonly string[] SourceUrlArgumentNames = ["fileUrl", "documentUrl", "imageUrl", "url", "file"];

    public static async Task<CallToolResult?> SaveOutputAsync(
        this RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        BinaryData content,
        string extension,
        string? folderUrl = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var resolvedTarget = await ResolveSourceOutputTargetAsync(
            requestContext,
            serviceProvider,
            normalizedExtension,
            cancellationToken);

        var uploadName = resolvedTarget.UploadName;
        var targetFolderUrl = string.IsNullOrWhiteSpace(folderUrl)
            ? resolvedTarget.FolderUrl
            : folderUrl;

        var uploaded = string.IsNullOrWhiteSpace(targetFolderUrl)
            ? await requestContext.Server.Upload(serviceProvider, uploadName, content, cancellationToken)
            : await UploadToFolderAsync(serviceProvider, requestContext, targetFolderUrl, uploadName, content, cancellationToken);

        return uploaded?.ToResourceLinkCallToolResponse();
    }

    public static (string Extension, BinaryData Content) ToSavedOutput(
        this JsonNode? result,
        string? requestedFormat,
        params string[] preferredFields)
    {
        var normalizedFormat = NormalizeExtension(requestedFormat);
        if (normalizedFormat == "json")
            return ("json", BinaryData.FromString(result?.ToJsonString() ?? "{}"));

        var extracted = TryExtractText(result, BuildCandidateFields(normalizedFormat, preferredFields));
        if (!string.IsNullOrWhiteSpace(extracted))
            return (normalizedFormat, BinaryData.FromString(extracted));

        return ("json", BinaryData.FromString(result?.ToJsonString() ?? "{}"));
    }

    private static async Task<ResourceLinkBlock?> UploadToFolderAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string folderUrl,
        string uploadName,
        BinaryData content,
        CancellationToken cancellationToken)
    {
        using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
        return await graphClient.UploadToFolder(folderUrl, uploadName, content, cancellationToken);
    }

    private static async Task<(string UploadName, string? FolderUrl)> ResolveSourceOutputTargetAsync(
        RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        string normalizedExtension,
        CancellationToken cancellationToken)
    {
        var fallbackUploadName = requestContext.ToOutputFileName(normalizedExtension);
        var sourceUrl = TryResolveSourceUrl(requestContext);

        if (string.IsNullOrWhiteSpace(sourceUrl))
            return (fallbackUploadName, null);

        var sourceNamedUpload = TryBuildSiblingOutputFileName(sourceUrl, normalizedExtension);
        if (!string.IsNullOrWhiteSpace(sourceNamedUpload))
            fallbackUploadName = sourceNamedUpload;

        try
        {
            using var graphClient = await serviceProvider.GetOboGraphClient(requestContext.Server);
            var siblingTarget = await graphClient.TryResolveSiblingOutputTargetAsync(sourceUrl, normalizedExtension, cancellationToken);
            if (siblingTarget is { } resolved)
                return resolved;
        }
        catch
        {
            // Best-effort sibling save. Fall back to default MCP output upload when source folder resolution is not available.
        }

        return (fallbackUploadName, null);
    }

    private static string NormalizeExtension(string? format)
        => (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "" => "json",
            "markdown" => "md",
            "text" => "txt",
            "html_split_page" => "html",
            var value => value
        };

    private static IReadOnlyList<string> BuildCandidateFields(string normalizedFormat, IEnumerable<string> preferredFields)
    {
        var fields = new List<string>();

        void Add(string? field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return;

            if (fields.Any(existing => string.Equals(existing, field, StringComparison.OrdinalIgnoreCase)))
                return;

            fields.Add(field);
        }

        Add(normalizedFormat);

        switch (normalizedFormat)
        {
            case "md":
                Add("markdown");
                break;
            case "txt":
                Add("text");
                break;
            case "html":
                Add("html");
                break;
        }

        foreach (var field in preferredFields)
            Add(field);

        Add("content");
        Add("output");
        Add("result");
        Add("text");
        Add("markdown");
        Add("html");
        Add("doctags");

        return fields;
    }

    private static string? TryExtractText(JsonNode? node, IReadOnlyList<string> candidateFields)
    {
        if (TryGetScalarText(node, out var directText))
            return directText;

        if (node is JsonObject obj)
        {
            foreach (var candidateField in candidateFields)
            {
                var match = obj.FirstOrDefault(property => string.Equals(property.Key, candidateField, StringComparison.OrdinalIgnoreCase));
                if (TryGetScalarText(match.Value, out var matchedText))
                    return matchedText;

                var nestedMatch = TryExtractText(match.Value, candidateFields);
                if (!string.IsNullOrWhiteSpace(nestedMatch))
                    return nestedMatch;
            }

            foreach (var property in obj)
            {
                var nested = TryExtractText(property.Value, candidateFields);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        if (node is JsonArray array)
        {
            var parts = new List<string>();
            foreach (var item in array)
            {
                if (TryGetScalarText(item, out var scalar))
                {
                    parts.Add(scalar);
                    continue;
                }

                var nested = TryExtractText(item, candidateFields);
                if (!string.IsNullOrWhiteSpace(nested))
                    parts.Add(nested);
            }

            if (parts.Count > 0)
                return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        return null;
    }

    private static bool TryGetScalarText(JsonNode? node, out string text)
    {
        text = string.Empty;

        if (node is not JsonValue value)
            return false;

            if (value.TryGetValue<string>(out var stringValue))
        {
            text = stringValue;
            return !string.IsNullOrWhiteSpace(text);
        }

        text = value.ToJsonString().Trim();
        return !string.IsNullOrWhiteSpace(text);
    }

    private static string? TryResolveSourceUrl(RequestContext<CallToolRequestParams> requestContext)
    {
        var arguments = requestContext.Params?.Arguments;
        if (arguments == null || arguments.Count == 0)
            return null;

        foreach (var candidateName in SourceUrlArgumentNames)
        {
            var argument = arguments.FirstOrDefault(arg => string.Equals(arg.Key, candidateName, StringComparison.OrdinalIgnoreCase));
            if (TryGetArgumentString(argument.Value, out var value))
                return value;
        }

        return null;
    }

    private static bool TryGetArgumentString(JsonElement argument, out string? value)
    {
        value = null;

        if (argument.ValueKind != JsonValueKind.String)
            return false;

        value = argument.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryBuildSiblingOutputFileName(string sourceUrl, string normalizedExtension)
    {
        if (!TryResolveSourceFileName(sourceUrl, out var sourceFileName))
            return null;

        return BuildSiblingOutputFileName(sourceFileName, normalizedExtension);
    }

    private static bool TryResolveSourceFileName(string sourceUrl, out string sourceFileName)
    {
        sourceFileName = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceUrl) || sourceUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            var lastSegment = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(lastSegment))
            {
                sourceFileName = Path.GetFileName(Uri.UnescapeDataString(lastSegment));
                return !string.IsNullOrWhiteSpace(sourceFileName);
            }
        }

        var directName = Path.GetFileName(sourceUrl.Trim());
        if (string.IsNullOrWhiteSpace(directName))
            return false;

        sourceFileName = directName;
        return true;
    }

    private static string BuildSiblingOutputFileName(string sourceFileName, string normalizedExtension)
        => $"{Path.GetFileName(sourceFileName.Trim())}.LLMs.{normalizedExtension}";
}
