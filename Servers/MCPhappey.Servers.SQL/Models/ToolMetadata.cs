using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(ServerId), nameof(ToolName))]
public class ToolMetadata
{
    [Column(Order = 1)]
    public int ServerId { get; set; }

    [Column(Order = 2)]
    public string ToolName { get; set; } = null!;

    [ForeignKey("ServerId")]
    public Server Server { get; set; } = null!;

    public string? OutputTemplate { get; set; }

}
