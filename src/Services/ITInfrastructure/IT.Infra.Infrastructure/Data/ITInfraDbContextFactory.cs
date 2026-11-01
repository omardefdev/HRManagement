using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IT.Infra.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class ITInfraDbContextFactory : IDesignTimeDbContextFactory<ITInfraDbContext>
{
    public ITInfraDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_ITINFRADB_CONNECTION")
            ?? "Host=127.0.0.1;Database=it_infra_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ITInfraDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(ITInfraDbContext).Assembly.GetName().Name));

        return new ITInfraDbContext(optionsBuilder.Options);
    }
}
