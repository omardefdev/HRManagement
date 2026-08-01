using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Compliance.Infrastructure.Data;

public class ComplianceDbContextFactory : IDesignTimeDbContextFactory<ComplianceDbContext>
{
    public ComplianceDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_COMPLIANCEDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=compliance_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ComplianceDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(ComplianceDbContext).Assembly.GetName().Name));

        return new ComplianceDbContext(optionsBuilder.Options);
    }
}
