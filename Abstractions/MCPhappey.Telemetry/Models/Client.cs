using Microsoft.EntityFrameworkCore;

namespace MCPHappey.Telemetry.Models;

[Index(nameof(ClientName), IsUnique = true)]
public class Client
{
    public int Id { get; set; }

    public string ClientName { get; set; } = null!;

    public ICollection<ClientVersion> Versions { get; set; } = [];
}
