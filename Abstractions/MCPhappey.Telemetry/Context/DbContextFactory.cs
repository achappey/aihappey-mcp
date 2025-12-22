using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MCPhappey.Telemetry.Context;

public class DbContextFactory : IDesignTimeDbContextFactory<MCPhappeyyTelemetryDatabaseContext>
{
  public MCPhappeyyTelemetryDatabaseContext CreateDbContext(string[] args)
  {
    if (args.Length != 1)
    {
      throw new InvalidOperationException("Please provide connection string like this: dotnet ef database update -- \"yourConnectionString\"");
    }

    var optionsBuilder = new DbContextOptionsBuilder<MCPhappeyyTelemetryDatabaseContext>();
    optionsBuilder.UseSqlServer(args[0], options => options.EnableRetryOnFailure());

    return new MCPhappeyyTelemetryDatabaseContext(optionsBuilder.Options);
  }
}
