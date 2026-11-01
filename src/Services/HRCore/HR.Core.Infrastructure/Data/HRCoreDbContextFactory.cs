using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class HRCoreDbContextFactory : IDesignTimeDbContextFactory<HRCoreDbContext>
{
    public HRCoreDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_HRCOREDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=hr_core_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<HRCoreDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name));

        return new HRCoreDbContext(optionsBuilder.Options);
    }
}
