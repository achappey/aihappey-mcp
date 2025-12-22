using MCPhappey.Servers.SQL.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Context;

public class McpDatabaseContext(DbContextOptions<McpDatabaseContext> options) : DbContext(options)
{
  public DbSet<Resource> Resources { get; set; } = null!;

  public DbSet<Server> Servers { get; set; } = null!;

  public DbSet<Prompt> Prompts { get; set; } = null!;

  public DbSet<PromptArgument> PromptArguments { get; set; } = null!;

  public DbSet<ResourceTemplate> ResourceTemplates { get; set; } = null!;

  public DbSet<ServerPlugin> Plugins { get; set; } = null!;
  
  public DbSet<ToolMetadata> ToolMetadata { get; set; } = null!;

  public DbSet<ServerOwner> ServerOwners { get; set; } = null!;

  public DbSet<ServerGroup> ServerGroups { get; set; } = null!;

  public DbSet<ServerApiKey> ServerApiKeys { get; set; } = null!;

  public DbSet<Icon> Icons { get; set; } = null!;

  public DbSet<ServerIcon> ServerIcons { get; set; } = null!;

  public DbSet<PromptIcon> PromptIcons { get; set; } = null!;

  public DbSet<ResourceIcon> ResourceIcons { get; set; } = null!;

  public DbSet<IconSize> IconSizes { get; set; } = null!;
  
  public DbSet<Size> Sizes { get; set; } = null!;

}
