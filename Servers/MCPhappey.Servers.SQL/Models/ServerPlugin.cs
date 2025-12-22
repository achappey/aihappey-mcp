using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[Table("Tools")]
[PrimaryKey(nameof(PluginName), nameof(ServerId))]
public class ServerPlugin
{
    [Column("Name", Order = 1)]
    public string PluginName { get; set; } = null!;

    [Column(Order = 2)]
    public int ServerId { get; set; }

    [ForeignKey("ServerId")]
    public Server Server { get; set; } = null!;


}
