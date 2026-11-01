using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Records.Archive.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class ArchiveDbContextFactory : IDesignTimeDbContextFactory<ArchiveDbContext>
{
    public ArchiveDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_ARCHIVEDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=archive_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ArchiveDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(ArchiveDbContext).Assembly.GetName().Name));

        return new ArchiveDbContext(optionsBuilder.Options);
    }
}
