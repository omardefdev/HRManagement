using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HRManagement.Gateway.Data;

/// <summary>
/// PLAN-38: design-time and migration-bundle construction without the web host. An efbundle otherwise runs the service's
/// Program up to Build(), which needs the deployment's wiring and secrets; the bootstrap passes only --connection.
/// </summary>
public class GatewayAuditDbContextFactory : IDesignTimeDbContextFactory<GatewayAuditDbContext>
{
    public GatewayAuditDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("HRMANAGEMENT_GATEWAYAUDITDB_CONNECTION")
            ?? "Host=127.0.0.1;Database=gateway_audit_design;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<GatewayAuditDbContext>();
        optionsBuilder.UseNpgsql(connection, npgsql =>
            npgsql.MigrationsAssembly(typeof(GatewayAuditDbContext).Assembly.FullName));

        return new GatewayAuditDbContext(optionsBuilder.Options);
    }
}
