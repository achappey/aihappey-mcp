using System.Text.Json.Nodes;
using MCPhappey.Common.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Core.Extensions;

public static class OcrOutputExtensions
{
    public static async Task<CallToolResult?> SaveOutputAsync(
        this RequestContext<CallToolRequestParams> requestContext,
        IServiceProvider serviceProvider,
        BinaryData content,
        string extension,
        string? folderUrl = null,
        CancellationToken cancellationToken = default)
    {
        var uploadName = requestContext.ToOutputFileName(NormalizeExtension(extension));

        var uploaded = string.IsNullOrWhiteSpace(folderUrl)
            ? await requestContext.Server.Upload(serviceProvider, uploadName, content, cancellationToken)
            : await UploadToFolderAsync(serviceProvider, requestContext, folderUrl, uploadName, content, cancellationToken);

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
}
