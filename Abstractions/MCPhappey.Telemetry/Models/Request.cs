namespace MCPHappey.Telemetry.Models;

public class Request
{
    public int Id { get; set; }

    public string SessionId { get; set; } = null!;

    public User? User { get; set; }

    public int? UserId { get; set; }

    public Server Server { get; set; } = null!;

    public int ServerId { get; set; }

    public Client Client { get; set; } = null!;

    public int ClientId { get; set; }

   // [Precision(3)]
    public DateTime StartedAt { get; set; }

    //[Precision(3)]
    public DateTime EndedAt { get; set; }

    public int OutputSize { get; set; }
}


public class PromptRequest : Request
{

}

public class ResourceRequest : Request
{
    public Resource Resource { get; set; } = null!;

    public int ResourceId { get; set; }

}

public class ToolRequest : Request
{
    public Tool Tool { get; set; } = null!;

    public int ToolId { get; set; }
}
