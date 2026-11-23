using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

/// <summary>
/// PLAN-53 (P-7): applies the scheduled salary changes whose effective date has come. One replica runs a cycle (an advisory lock); the
/// agencies are found in the audit log, which is outside row security and holds a row for every saved request from the request's own
/// save; each agency runs in its own scope under its own tenant, as the system actor, and one agency's failure does not hide the
/// others. A refused request stays scheduled with its reason, and the gauge <c>payroll_salary_change_apply_blocked</c> counts them.
/// The plan's daily cron is an interval here (<c>Payroll:SalaryChange:Activation:IntervalMinutes</c>): the repository has no cron
/// library, and an hourly pass applies a change on its first day whatever the hour the service started.
/// </summary>
public sealed class PayProfileChangeActivationJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PayProfileChangeOptions> options,
    ILogger<PayProfileChangeActivationJob> logger) : BackgroundService
{
    private TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Activation.IntervalMinutes));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the first pass so a rolling deploy does not have every replica contend at once.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Activation.Enabled)
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogError(ex, "Salary change activation cycle failed."); }
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>One cycle, if this replica holds the lock. Returns the summary of each agency it ran.</summary>
    public async Task<IReadOnlyDictionary<Guid, PayProfileChangeActivationSummary>> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new Dictionary<Guid, PayProfileChangeActivationSummary>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db, AdvisoryLockKeys.PayProfileChangeActivation, ct => ActivateAllTenantsAsync(db, summaries, ct), cancellationToken);
        return summaries;
    }

    /// <summary>The agencies that ever saved a salary change request: the audit log is outside row security, and the interceptor writes
    /// the request's audit row in the request's own save.</summary>
    public static Task<List<Guid>> FindTenantsAsync(PayrollDbContext db, CancellationToken ct = default) =>
        db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == nameof(PayProfileChangeRequest) && l.TenantId != Guid.Empty)
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync(ct);

    private async Task ActivateAllTenantsAsync(
        PayrollDbContext db, Dictionary<Guid, PayProfileChangeActivationSummary> summaries, CancellationToken ct)
    {
        var tenants = await FindTenantsAsync(db, ct);
        var (applied, blocked) = (0, 0);
        foreach (var tenantId in tenants)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenantId);
            try
            {
                // Its own scope: the agency's requests are read and written under its tenant only (the query filter and row security).
                using var tenantScope = scopeFactory.CreateScope();
                var activator = tenantScope.ServiceProvider.GetRequiredService<PayProfileChangeActivator>();
                var summary = await activator.ActivateDueAsync(ct);
                PayProfileChangeMetrics.SetBlocked(tenantId, summary.BlockedTotal);
                summaries[tenantId] = summary;
                applied += summary.Applied;
                blocked += summary.BlockedTotal;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One agency must not hide the others.
                logger.LogError(ex, "Salary change activation failed for tenant {TenantId}.", tenantId);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        logger.LogInformation(
            "Salary change activation: {Tenants} tenant(s), {Applied} applied, {Blocked} scheduled and blocked.", tenants.Count, applied, blocked);
    }
}
