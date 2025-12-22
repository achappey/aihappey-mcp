using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(PromptId), nameof(IconId))]
public class PromptIcon
{
    public int PromptId { get; set; }

    public Prompt Prompt { get; set; } = null!;

    public int IconId { get; set; }

    public Icon Icon { get; set; } = null!;

}
