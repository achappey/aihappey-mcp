using System.Text;
using HtmlAgilityPack;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.DataFormats.Office;
using Microsoft.KernelMemory.DataFormats.Pdf;
using MimeKit;

namespace MCPhappey.Decoders;

public class EmlDecoder : IContentDecoder
{
    List<IContentDecoder> defaultDecoders = [new PdfDecoder(), new MsWordDecoder(), new MsExcelDecoder()];

    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null &&
               (mimeType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase) ||
                mimeType.EndsWith(".eml", StringComparison.OrdinalIgnoreCase));
    }

    public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filename);
        return DecodeAsync(stream, cancellationToken);
    }

    public Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        using var stream = data.ToStream();
        return DecodeAsync(stream, cancellationToken);
    }

    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        var message = await MimeMessage.LoadAsync(data, cancellationToken);

        var sb = new StringBuilder();

        // --- 1️⃣ Headers ---
        sb.AppendLine($"From: {message.From}");
        sb.AppendLine($"To: {message.To}");
        if (message.Cc?.Any() == true)
            sb.AppendLine($"Cc: {message.Cc}");
        sb.AppendLine($"Date: {message.Date}");
        sb.AppendLine($"Subject: {message.Subject}");
        sb.AppendLine();
        // --- 2️⃣ Body text extraction ---
        string? bodyText = GetBodyText(message);

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            sb.AppendLine(bodyText.Trim());
        }

        var result = new FileContent(Microsoft.KernelMemory.Pipeline.MimeTypes.PlainText);
        result.Sections.Add(new Chunk(sb.ToString(), 0, Chunk.Meta(sentencesAreComplete: true)));

        // --- 3️⃣ Attachments (add as separate FileItems) ---
        // --- 3️⃣ Attachments (decode using available decoders or fallback) ---
        foreach (var attachment in message.Attachments)
        {
            string? fileName = null;
            string mime = "application/octet-stream";
            byte[] bytes;

            if (attachment is MimePart part)
            {
                fileName = part.FileName;
                mime = part.ContentType?.MimeType ?? mime;
                await using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }
            else if (attachment is MessagePart nested)
            {
                fileName = nested.ContentId ?? "attached-message.eml";
                mime = nested.ContentType?.MimeType ?? "message/rfc822";
                await using var ms = new MemoryStream();
                await nested.Message.WriteToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }
            else
            {
                continue;
            }

            // 3a) Try to find a decoder from the injected list
            var decoder = defaultDecoders.FirstOrDefault(d => d.SupportsMimeType(mime));

            string? textContent = null;

           if (decoder != null)
            {
                try
                {
                    var decoded = await decoder.DecodeAsync(BinaryData.FromBytes(bytes), cancellationToken);
                    if (decoded != null)
                    {
                        textContent = string.Join("\n", decoded.Sections.Select(s => s.Content));
                    }

                }
                catch
                {
                    // fallback to manual decode if decoder fails
                }
            }

            // 3b) Fallback text extraction (simple MIME heuristics)
            if (string.IsNullOrWhiteSpace(textContent))
            {
                if (mime.StartsWith("text/") || mime.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    textContent = Encoding.UTF8.GetString(bytes);
                }
                else if (mime.Equals("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(Encoding.UTF8.GetString(bytes));
                    textContent = string.Join(
                        "\n",
                        htmlDoc.DocumentNode
                            .Descendants()
                            .Where(n => n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(n.InnerText))
                            .Select(n => n.InnerText.Trim())
                    );
                }
            }

            // 3c) Add to the result
            if (!string.IsNullOrWhiteSpace(textContent))
            {
                result.Sections.Add(new Chunk(
                    $"[Attachment: {fileName ?? "attachment"}]\n{textContent}",
                    0,
                    Chunk.Meta(sentencesAreComplete: true)));
            }
        }

        return result;
    }

    private static string? GetBodyText(MimeMessage message)
    {
        // If plain text exists, use it directly
        if (message.TextBody != null)
            return message.TextBody;

        // Otherwise, fall back to HTML body → extract visible text
        if (message.HtmlBody != null)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(message.HtmlBody);

            foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
                node.Remove();

            var text = string.Join(
                "\n",
                doc.DocumentNode.Descendants()
                    .Where(n => n.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(n.InnerText))
                    .Select(n => n.InnerText.Trim())
            );

            return text;
        }

        // Otherwise, try multipart fallback
        if (message.Body is Multipart multipart)
        {
            var builder = new StringBuilder();

            foreach (var part in multipart)
            {
                if (part is TextPart textPart)
                {
                    builder.AppendLine(textPart.Text);
                }
                else if (part is MessagePart msgPart)
                {
                    // nested email
                    var nestedText = GetBodyText(msgPart.Message);
                    if (!string.IsNullOrWhiteSpace(nestedText))
                        builder.AppendLine(nestedText);
                }
            }

            return builder.ToString();
        }

        return null;
    }
}
