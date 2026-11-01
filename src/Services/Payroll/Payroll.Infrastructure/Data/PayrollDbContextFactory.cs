using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payroll.Infrastructure.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class PayrollDbContextFactory : IDesignTimeDbContextFactory<PayrollDbContext>
{
    public PayrollDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_PAYROLLDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=payroll_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<PayrollDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name));

        return new PayrollDbContext(optionsBuilder.Options);
    }
}
