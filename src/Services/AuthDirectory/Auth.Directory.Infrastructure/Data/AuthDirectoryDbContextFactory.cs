using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Directory.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class AuthDirectoryDbContextFactory : IDesignTimeDbContextFactory<AuthDirectoryDbContext>
{
    public AuthDirectoryDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_AUTHDIRECTORYDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=auth_directory_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AuthDirectoryDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(AuthDirectoryDbContext).Assembly.GetName().Name));

        return new AuthDirectoryDbContext(optionsBuilder.Options);
    }
}
