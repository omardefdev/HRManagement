using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Admin.Logistics.Infrastructure.Data;

public class AdminLogisticsDbContextFactory : IDesignTimeDbContextFactory<AdminLogisticsDbContext>
{
    public AdminLogisticsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_ADMINDDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=admin_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AdminLogisticsDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(AdminLogisticsDbContext).Assembly.GetName().Name));

        return new AdminLogisticsDbContext(optionsBuilder.Options);
    }
}
