using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(ResourceId), nameof(IconId))]
public class ResourceIcon
{
    public int ResourceId { get; set; }

    public Resource Resource { get; set; } = null!;

    public int IconId { get; set; }

    public Icon Icon { get; set; } = null!;

}
