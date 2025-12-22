using Microsoft.EntityFrameworkCore;

namespace MCPHappey.Telemetry.Models;

[Index(nameof(Name), IsUnique = true)]
public class Server
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public ICollection<Request> Requests { get; set; } = [];
}
