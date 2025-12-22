using Microsoft.EntityFrameworkCore;
using MCPHappey.Telemetry.Models;

namespace MCPhappey.Telemetry.Context;

public class MCPhappeyyTelemetryDatabaseContext(DbContextOptions<MCPhappeyyTelemetryDatabaseContext> options) : DbContext(options)
{
  public DbSet<Server> Servers { get; set; } = null!;

  public DbSet<User> Users { get; set; } = null!;

  public DbSet<PromptRequest> PromptRequests { get; set; } = null!;

  public DbSet<ResourceRequest> ResourceRequests { get; set; } = null!;

  public DbSet<Tool> Tools { get; set; } = null!;

  public DbSet<Resource> Resources { get; set; } = null!;

  public DbSet<ToolRequest> ToolRequests { get; set; } = null!;

  public DbSet<Client> Clients { get; set; } = null!;

  public DbSet<ClientVersion> ClientVersions { get; set; } = null!;

}
