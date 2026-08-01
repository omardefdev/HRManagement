using HRManagement.Gateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HRManagement.Gateway.Services;

/// <summary>
/// Proves the SIEM detector can actually do its job before the gateway reports itself ready.
/// <para>
/// A plain DbContext check only opens a connection, so the gateway answered <c>/health/ready</c>
/// with 200 while the SIEM detector failed every cycle against a <c>security_audit_events</c>
/// table that a skipped migration had never created. Readiness has to fail when a security
/// control is silently down, otherwise the deployment looks healthy while it is blind.
/// </para>
/// </summary>
public sealed class SecurityAuditSchemaHealthCheck(GatewayAuditDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Touch the table the detector queries. A missing relation throws here rather than
            // being swallowed by the detector's background loop.
            _ = await db.SecurityAuditEvents
                .AsNoTracking()
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return HealthCheckResult.Healthy("security_audit_events is queryable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "SIEM store is not queryable; the security detector cannot run. "
                + "Check that the audit migrations were applied.",
                ex);
        }
    }
}
