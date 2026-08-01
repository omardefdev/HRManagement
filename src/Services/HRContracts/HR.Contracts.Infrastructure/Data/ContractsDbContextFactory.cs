using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Contracts.Infrastructure.Data;

public class ContractsDbContextFactory : IDesignTimeDbContextFactory<ContractsDbContext>
{
    public ContractsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ContractsDbContext>();
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_CONTRACTSDB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Set HRMANAGEMENT_CONTRACTSDB_CONNECTION for design-time migrations");

        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name));

        return new ContractsDbContext(optionsBuilder.Options);
    }
}
