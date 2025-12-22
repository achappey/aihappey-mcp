using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Web;
using MCPhappey.Common.Models;
using Microsoft.Graph.Beta;
using Microsoft.Graph.Beta.Models;

namespace MCPhappey.Scrapers.Extensions;

public static partial class OutlookExtensions
{

    public static bool TryParse(string url, out string? mailbox, out string? itemId)
    {
        mailbox = null;
        itemId = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Geen geldige URL.", nameof(url));

        if (!uri.Host.EndsWith("outlook.office.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith("outlook.office365.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Geen Outlook-Web URL.", nameof(url));

        // 1. mailbox (next segment after “mail” or “owa”)
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var anchor = Array.FindIndex(segments,
                         s => s.Equals("mail", StringComparison.OrdinalIgnoreCase) ||
                              s.Equals("owa", StringComparison.OrdinalIgnoreCase));
        if (anchor >= 0 && anchor + 1 < segments.Length)
        {
            var candidate = Uri.UnescapeDataString(segments[anchor + 1]);
            if (candidate.Contains('@')) mailbox = candidate;
        }

        // 2a. /id/<ItemId>
        var m = PathIdRegex().Match(uri.AbsolutePath);
        if (m.Success)
        {
            itemId = m.Groups["id"].Value;   // ← keep the encoded form
            return true;
        }

        // 2b. ?ItemID=… or ?id=…
        var qs = HttpUtility.ParseQueryString(uri.Query);
        itemId = qs["ItemID"] ?? qs["id"]; // already encoded
        return !string.IsNullOrEmpty(itemId);
    }


    public static bool TryParse22(string url, out string? mailbox, out string? itemId)
    {
        mailbox = null;
        itemId = null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Geen geldige URL.", nameof(url));

        if (!uri.Host.EndsWith("outlook.office.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith("outlook.office365.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Geen Outlook-Web URL.", nameof(url));

        // 1. mailbox uit pad
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var anchor = Array.FindIndex(segments,
                        s => s.Equals("mail", StringComparison.OrdinalIgnoreCase) ||
                             s.Equals("owa", StringComparison.OrdinalIgnoreCase));

        if (anchor >= 0 && anchor + 1 < segments.Length)
        {
            var candidate = Uri.UnescapeDataString(segments[anchor + 1]);
            if (candidate.Contains('@')) mailbox = candidate;
        }

        // 2a. /id/<ItemId>
        var match = PathIdRegex().Match(uri.AbsolutePath);
        if (match.Success)
        {
            itemId = Uri.UnescapeDataString(match.Groups["id"].Value);
            return true;
        }

        // 2b. ?ItemID=...  of  ?id=...
        var qs = HttpUtility.ParseQueryString(uri.Query);
        var raw = qs["ItemID"] ?? qs["id"];
        if (!string.IsNullOrEmpty(raw))
        {
            itemId = Uri.UnescapeDataString(raw);
            return true;
        }

        return false;
    }

    public static FileItem? CreateBodyFileItem(this Message msg)
    {
        if (msg.Body?.Content == null || msg.Body.Content.Length == 0)
            return null;

        var filename = $"{msg.Id}.{(msg.Body.ContentType is BodyType.Html ? "html" : "txt")}";
        var mime = msg.Body.ContentType is BodyType.Html
            ? MediaTypeNames.Text.Html
            : MediaTypeNames.Text.Plain;

        return new FileItem
        {
            Contents = BinaryData.FromString(msg.Body.Content),
            Filename = filename,
            MimeType = mime,
            Uri = msg.WebLink ?? string.Empty
        };
    }

    public static async Task<FileItem?> CreateAttachmentFileItemAsync(
        this GraphServiceClient graph,
        string? mailbox,
        string messageId,
        FileAttachment attachment,
        CancellationToken cancellationToken)
    {
        byte[]? contentBytes = attachment.ContentBytes;
        FileAttachment effectiveAttachment = attachment;

        if (contentBytes == null || contentBytes.Length == 0)
        {
            var fetched = string.IsNullOrEmpty(mailbox)
                ? await graph.Me.Messages[messageId]
                    .Attachments[attachment.Id!]
                    .GetAsync(cancellationToken: cancellationToken)
                : await graph.Users[mailbox].Messages[messageId]
                    .Attachments[attachment.Id!]
                    .GetAsync(cancellationToken: cancellationToken);

            if (fetched is FileAttachment fetchedFile && fetchedFile.ContentBytes is { Length: > 0 })
            {
                effectiveAttachment = fetchedFile;
                contentBytes = fetchedFile.ContentBytes;
            }
        }

        if (contentBytes == null || contentBytes.Length == 0)
            return null;

        return new FileItem
        {
            Contents = BinaryData.FromBytes(contentBytes),
            Filename = effectiveAttachment.Name,
            MimeType = effectiveAttachment.ContentType ?? MediaTypeNames.Application.Octet,
            Uri = effectiveAttachment.ContentLocation ?? string.Empty
        };
    }

    [GeneratedRegex(@"/id/(?<id>[^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "nl-NL")]
    private static partial Regex PathIdRegex();

    //   [GeneratedRegex(@"/id/(?<id>[A-Za-z0-9\-._~%]+=*)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "nl-NL")]
    //  private static partial Regex PathIdRegex();
}

