using Microsoft.EntityFrameworkCore;

namespace MCPHappey.Telemetry.Models;

[Index(nameof(ToolName), IsUnique = true)]
public class Tool
{
    public int Id { get; set; }

    public string ToolName { get; set; } = null!;

    public ICollection<ToolRequest> ToolRequests { get; set; } = [];
}
