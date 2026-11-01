using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IDCard.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class IDCardDbContextFactory : IDesignTimeDbContextFactory<IDCardDbContext>
{
    public IDCardDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_IDCARDDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=idcard_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<IDCardDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(IDCardDbContext).Assembly.GetName().Name));

        return new IDCardDbContext(optionsBuilder.Options);
    }
}
