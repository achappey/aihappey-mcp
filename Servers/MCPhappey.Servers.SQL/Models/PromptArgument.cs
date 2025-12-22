namespace MCPhappey.Servers.SQL.Models;

public class PromptArgument
{
    public int Id { get; set; }

    public int PromptId { get; set; }

    public Prompt Prompt { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string? Format { get; set; }

    public string? Type { get; set; }

    public bool? Required { get; set; }
}
