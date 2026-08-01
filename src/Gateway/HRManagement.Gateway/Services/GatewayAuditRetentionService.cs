using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRManagement.Gateway.Services;

/// <summary>
/// Optional daily purge of old gateway audit rows. Disabled by default.
/// OBS-003: requires AllowPurge, no active legal hold on range, security event + WORM export first.
/// </summary>
public sealed class GatewayAuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditRetentionOptions> options,
    ISecurityAuditPublisher securityAudit,
    AuditWormExporter wormExporter,
    ILogger<GatewayAuditRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var opts = options.Value;
            if (!opts.Enabled || opts.RetentionDays <= 0)
                continue;

            if (!opts.AllowPurge)
            {
                logger.LogWarning(
                    "Audit retention Enabled but AllowPurge=false; skipping DELETE (OBS-003).");
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
                await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                    db,
                    AdvisoryLockKeys.GatewayAuditRetention,
                    async ct =>
                    {
                        var innerOpts = options.Value;
                        if (!innerOpts.Enabled || innerOpts.RetentionDays <= 0)
                            return;

                        if (!innerOpts.AllowPurge)
                        {
                            logger.LogWarning(
                                "Audit retention Enabled but AllowPurge=false; skipping DELETE (OBS-003).");
                            return;
                        }

                        var cutoff = DateTimeOffset.UtcNow.AddDays(-innerOpts.RetentionDays);

                        var holds = await db.AuditLegalHolds.AsNoTracking()
                            .Where(h => h.Active)
                            .ToListAsync(ct);

                        if (holds.Any(h => HoldBlocksPurge(h, cutoff)))
                        {
                            logger.LogWarning(
                                "Audit retention blocked by active legal hold (cutoff={Cutoff:u}).",
                                cutoff);
                            return;
                        }

                        await wormExporter.ExportAsync(ct);

                        await securityAudit.PublishAsync(new SecurityAuditEvent
                        {
                            EventType = SecurityAuditEventTypes.AuditPurge,
                            Resource = "gateway_audit_entries",
                            DetailJson = $"{{\"cutoff\":\"{cutoff:O}\",\"retentionDays\":{innerOpts.RetentionDays}}}",
                            OccurredAtUtc = DateTimeOffset.UtcNow
                        }, ct);

                        var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                            $"DELETE FROM gateway_audit_entries WHERE \"OccurredAtUtc\" < {cutoff}",
                            ct);

                        logger.LogInformation(
                            "Audit retention deleted {Count} entries older than {Cutoff:u} (RetentionDays={Days}).",
                            deleted, cutoff, innerOpts.RetentionDays);
                    },
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Audit retention job failed.");
            }
        }
    }

    internal static bool HoldBlocksPurge(AuditLegalHold hold, DateTimeOffset cutoff)
    {
        // Any open-ended or overlapping hold that could cover rows older than cutoff blocks purge.
        if (hold.RangeStartUtc is null && hold.RangeEndUtc is null &&
            string.IsNullOrEmpty(hold.CorrelationId) && string.IsNullOrEmpty(hold.UserSub))
            return true; // global hold

        if (hold.RangeStartUtc is null && hold.RangeEndUtc is null)
            return true; // actor/correlation scoped — still block bulk purge for safety

        if (hold.RangeEndUtc is null || hold.RangeEndUtc >= cutoff)
            return true;

        return false;
    }
}
