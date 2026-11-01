using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace File.Storage.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class FileStorageDbContextFactory : IDesignTimeDbContextFactory<FileStorageDbContext>
{
    public FileStorageDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_FILESTORAGEDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=file_storage_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<FileStorageDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(FileStorageDbContext).Assembly.GetName().Name));

        return new FileStorageDbContext(optionsBuilder.Options);
    }
}
