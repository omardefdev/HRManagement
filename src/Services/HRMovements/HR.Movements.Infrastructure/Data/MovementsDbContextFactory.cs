using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Movements.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class MovementsDbContextFactory : IDesignTimeDbContextFactory<MovementsDbContext>
{
    public MovementsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_MOVEMENTSDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=movements_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<MovementsDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(MovementsDbContext).Assembly.GetName().Name));

        return new MovementsDbContext(optionsBuilder.Options);
    }
}
