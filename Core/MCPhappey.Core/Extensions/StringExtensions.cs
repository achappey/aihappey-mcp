
using System.Net.Mime;
using MCPhappey.Common.Models;

namespace MCPhappey.Core.Extensions;

public static class StringExtensions
{
    public static bool HasResult(this string? result)
         => !string.IsNullOrEmpty(result) && !result.Contains("INFO NOT FOUND", StringComparison.OrdinalIgnoreCase);

    public static string CleanJson(this string input)
    {
        const string prefix = "```json";
        const string suffix = "```";

        if (input.StartsWith(prefix) && input.EndsWith(suffix))
        {
            return input.Substring(prefix.Length, input.Length - prefix.Length - suffix.Length).Trim();
        }

        return input;
    }

    public static string GetServerNameFromUrl(this string url)
    {
        var trimmedUrl = url.TrimStart('/');

        return trimmedUrl.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.ToLowerInvariant() ?? string.Empty;
    }

    public static FileItem ToFileItem(this string content,
        string uri,
        string mimeType = MediaTypeNames.Text.Plain)
            => new()
            {
                Contents = BinaryData.FromString(content),
                MimeType = mimeType,
                Uri = uri,
            };

    public static FileItem ToJsonFileItem(this string content, string uri)
        => content.ToFileItem(uri, MediaTypeNames.Application.Json);

}