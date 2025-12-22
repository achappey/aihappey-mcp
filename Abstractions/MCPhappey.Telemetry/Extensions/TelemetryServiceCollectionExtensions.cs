using MCPhappey.Telemetry.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MCPhappey.Telemetry.Extensions;

public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the telemetry DbContext and ChatTelemetryService in DI.
    /// </summary>
    public static IServiceCollection AddTelemetryServices(
        this IServiceCollection services,
        string connectionString)
    {
        // DbContext registration (configure from appsettings)
        services.AddDbContext<MCPhappeyyTelemetryDatabaseContext>(options =>
            options.UseSqlServer(connectionString)); // or UseNpgsql etc.

        // your service
        services.AddScoped<IMcpTelemetryService, McpTelemetryService>();

        return services;
    }
}
