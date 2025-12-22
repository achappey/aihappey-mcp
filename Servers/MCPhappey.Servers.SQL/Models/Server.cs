using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[Index(nameof(Name), IsUnique = true)]
public class Server
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? WebsiteUrl { get; set; }

    public bool Secured { get; set; }

    public bool? Hidden { get; set; }

    public ICollection<Prompt> Prompts { get; set; } = [];

    public ICollection<Resource> Resources { get; set; } = [];

    public ICollection<ResourceTemplate> ResourceTemplates { get; set; } = [];

    public ICollection<ServerOwner> Owners { get; set; } = [];

    public ICollection<ServerGroup> Groups { get; set; } = [];

    public ICollection<ServerPlugin> Plugins { get; set; } = [];

    public ICollection<ServerApiKey> ApiKeys { get; set; } = [];

    public string? Instructions { get; set; }

    public ICollection<ServerIcon> Icons { get; set; } = [];

    public ICollection<ToolMetadata> Tools { get; set; } = [];

    public bool? ToolPrompts { get; set; }

}
