using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MCPhappey.Servers.SQL.Context;

public class DbContextFactory : IDesignTimeDbContextFactory<McpDatabaseContext>
{
  public McpDatabaseContext CreateDbContext(string[] args)
  {
    if (args.Length != 1)
    {
      throw new InvalidOperationException("Please provide connection string like this: dotnet ef database update -- \"yourConnectionString\"");
    }

    var optionsBuilder = new DbContextOptionsBuilder<McpDatabaseContext>();
    optionsBuilder.UseSqlServer(args[0], options => options.EnableRetryOnFailure());

    return new McpDatabaseContext(optionsBuilder.Options);
  }
}
