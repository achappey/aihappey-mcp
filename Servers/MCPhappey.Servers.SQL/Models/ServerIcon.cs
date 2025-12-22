using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(ServerId), nameof(IconId))]
public class ServerIcon
{
    public int ServerId { get; set; }

    public Server Server { get; set; } = null!;

    public int IconId { get; set; }

    public Icon Icon { get; set; } = null!;

}
