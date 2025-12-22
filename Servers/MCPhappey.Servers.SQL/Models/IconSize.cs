using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(SizeId), nameof(IconId))]
public class IconSize
{
    public int SizeId { get; set; }

    public Size Size { get; set; } = null!;

    public int IconId { get; set; }

    public Icon Icon { get; set; } = null!;

}
