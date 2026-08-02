using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Contracts.Infrastructure.Data;

public class ContractsDbContextFactory : IDesignTimeDbContextFactory<ContractsDbContext>
{
    public ContractsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ContractsDbContext>();
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_CONTRACTSDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=contracts_design;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name));

        return new ContractsDbContext(optionsBuilder.Options);
    }
}
