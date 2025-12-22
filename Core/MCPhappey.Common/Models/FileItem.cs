namespace MCPhappey.Common.Models;

public class FileItem
{
    public BinaryData Contents { get; set; } = null!;

    public string Uri { get; set; } = null!;

    public string? Filename { get; set; }

    public string MimeType { get; set; } = null!;

}