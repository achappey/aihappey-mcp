using System.Text.Json.Nodes;
using MCPhappey.Common.Models;

namespace MCPhappey.Tools.OpenAI.Decisions;

/// <summary>Shares authenticated RAG extraction and raw downloads while keeping the two paths distinct.</summary>
public sealed class OpenAIDecisionsInputBuilder(
    Func<string, CancellationToken, Task<IEnumerable<FileItem>>> scrape,
    Func<string, CancellationToken, Task<IEnumerable<FileItem>>> download)
{
    public const int MaximumImages = 128;

    public async Task<JsonNode> BuildAsync(
        string? inputText, string[]? documentUrls, string[]? imageUrls,
        string imageDetail = "auto", CancellationToken cancellationToken = default)
    {
        ValidateUrls(documentUrls);
        ValidateUrls(imageUrls);
        if (imageUrls is { Length: > MaximumImages })
            throw new ArgumentException($"At most {MaximumImages} images can be supplied.");
        if (imageDetail is not ("auto" or "low" or "high" or "original"))
            throw new ArgumentException("imageDetail must be auto, low, high, or original.");

        var texts = new List<string>();
        if (!string.IsNullOrWhiteSpace(inputText))
            texts.Add(inputText);

        foreach (var url in documentUrls ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = await scrape(url, cancellationToken);
            var text = string.Join("\n\n", files.GetTextFiles()
                .Select(file => file.Contents.ToString()).Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("A document URL returned no extractable text. Use imageUrls for image evidence.");
            texts.Add(text);
        }

        var content = new JsonArray();
        if (texts.Count > 0)
            content.Add(new JsonObject { ["type"] = "input_text", ["text"] = string.Join("\n\n", texts) });

        var imageCount = 0;
        foreach (var url in imageUrls ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = (await download(url, cancellationToken)).ToList();
            if (files.Count == 0)
                throw new InvalidOperationException("An image URL returned no file content.");

            foreach (var file in files)
            {
                if (++imageCount > MaximumImages)
                    throw new ArgumentException($"At most {MaximumImages} images can be supplied.");
                content.Add(new JsonObject
                {
                    ["type"] = "input_image",
                    ["image_url"] = ToImageDataUrl(file),
                    ["detail"] = imageDetail
                });
            }
        }

        if (content.Count == 0)
            throw new ArgumentException("Provide inputText, documentUrls, or imageUrls containing usable evidence.");

        // Keep text-only requests simple; native images require a user message with content parts.
        return imageCount == 0
            ? JsonValue.Create(string.Join("\n\n", texts))!
            : new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content });
    }

    public async Task<JsonArray> LoadQuestionsAsync(string questionsUrl, CancellationToken cancellationToken = default)
    {
        ValidateUrl(questionsUrl);
        var files = (await download(questionsUrl, cancellationToken)).ToList();
        if (files.Count != 1 || files[0].Contents.IsEmpty)
            throw new InvalidOperationException("The questions URL must return exactly one nonempty JSON file.");
        return OpenAIDecisionsQuestions.FromDocument(files[0].Contents.ToString());
    }

    private static void ValidateUrls(string[]? urls)
    {
        foreach (var url in urls ?? [])
            ValidateUrl(url);
    }

    private static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("URLs must be absolute HTTP(S) URLs without embedded credentials.");
    }

    private static string ToImageDataUrl(FileItem file)
    {
        // Sniff the payload, not the URL extension/MIME alone: protected links can return login HTML.
        var bytes = file.Contents.ToMemory().Span;
        string mimeType;
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            mimeType = "image/png";
        else if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
            mimeType = "image/jpeg";
        else if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            mimeType = "image/gif";
        else if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            mimeType = "image/webp";
        else
            throw new ArgumentException("Image URLs must return PNG, JPEG, GIF, or WebP image bytes, not a web page or unsupported file.");

        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
