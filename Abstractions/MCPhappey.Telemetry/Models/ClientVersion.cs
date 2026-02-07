namespace MCPHappey.Telemetry.Models;

public class ClientVersion
{
    public int Id { get; set; }

    public string Version { get; set; } = null!;

    public Client Client { get; set; } = null!;

    public int ClientId { get; set; }
}
