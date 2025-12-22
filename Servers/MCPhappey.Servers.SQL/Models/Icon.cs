namespace MCPhappey.Servers.SQL.Models;

public class Icon
{
    public int Id { get; set; }

    public string Source { get; set; } = null!;

    public string? MimeType { get; set; }

    public string? Theme { get; set; }

    public ICollection<IconSize> Sizes { get; set; } = [];

    public ICollection<ServerIcon> ServerIcons { get; set; } = [];

    public ICollection<ResourceIcon> ResourceIcons { get; set; } = [];

    public ICollection<PromptIcon> PromptIcons { get; set; } = [];
}
