namespace MCPhappey.Servers.SQL.Models;

public class Prompt
{
    public int Id { get; set; }

    public int ServerId { get; set; }

    public Server Server { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string PromptTemplate { get; set; } = null!;

    public ICollection<PromptArgument> Arguments { get; set; } = [];

    public ICollection<PromptIcon> Icons { get; set; } = [];
}
