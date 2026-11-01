using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Health.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class HealthDbContextFactory : IDesignTimeDbContextFactory<HealthDbContext>
{
    public HealthDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_HEALTHDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=health_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<HealthDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(HealthDbContext).Assembly.GetName().Name));

        return new HealthDbContext(optionsBuilder.Options);
    }
}
