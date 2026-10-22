using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Infrastructure.Services;

/// <summary>FUN-029 (J): commencement watch settings (<c>Recruitment</c> section of HR.Contracts).</summary>
public sealed class HireCommencementOptions
{
    public const string SectionName = "Recruitment";

    /// <summary><c>Recruitment:CommencementDeadlineDays</c> — days after the expected start date (default 30).</summary>
    public int CommencementDeadlineDays { get; set; } = 30;

    /// <summary><c>Recruitment:Jobs:CommencementWatchEnabled</c> (default true; false in test hosts).</summary>
    public HireCommencementJobOptions Jobs { get; set; } = new();
}

public sealed class HireCommencementJobOptions
{
    public bool CommencementWatchEnabled { get; set; } = true;

    /// <summary><c>Recruitment:Jobs:CommencementWatchIntervalMinutes</c> (default 1440, daily).</summary>
    public int CommencementWatchIntervalMinutes { get; set; } = 1440;
}

/// <summary>
/// FUN-029 (J): flags open Appointment transactions whose employee has no commencement record by
/// ExpectedStartDate + CommencementDeadlineDays — sets <c>CommencementOverdueAt</c> once and opens an intervention.
/// Detection only.
/// PLAN-29 follow-up: cancel hire for non-commencement → vacate position, reopen requisition, recall reserve (see PLAN-27 marker)
/// </summary>
public sealed class HireCommencementWatcher(
    ContractsDbContext db,
    IEventPublisher eventPublisher,
    IOptions<HireCommencementOptions> options,
    ILogger<HireCommencementWatcher> logger)
{
    public async Task<int> FlagOverdueAsync(DateOnly todayUtc, CancellationToken ct = default)
    {
        var deadline = todayUtc.AddDays(-Math.Max(0, options.Value.CommencementDeadlineDays));

        var candidates = await db.ServiceTransactions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(t => t.TransactionType == ServiceTransactionType.Appointment
                        && t.State != ServiceTransactionState.Archived
                        && t.State != ServiceTransactionState.Cancelled
                        && t.State != ServiceTransactionState.Completed
                        && t.CommencementOverdueAt == null
                        && t.ExpectedStartDate != null
                        && t.ExpectedStartDate < deadline)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var employeeIds = candidates.Select(t => t.EmployeeId).Distinct().ToList();
        var commenced = (await db.CommencementRecords
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .Where(c => employeeIds.Contains(c.EmployeeId))
                .Select(c => c.EmployeeId)
                .ToListAsync(ct))
            .ToHashSet();

        var flagged = 0;
        var nowUtc = DateTime.UtcNow;
        foreach (var transaction in candidates.Where(t => !commenced.Contains(t.EmployeeId)))
        {
            transaction.CommencementOverdueAt = nowUtc;
            await eventPublisher.PublishProcessInterventionRequestedAsync(
                SagaTypeNames.Hire,
                transaction.Id,
                HireContractMessages.CommencementOverdue,
                "HR.Contracts",
                transaction.TenantId == Guid.Empty ? null : transaction.TenantId,
                ct);
            flagged++;
        }

        if (flagged == 0)
            return 0;

        await db.SaveChangesAsync(ct);
        HireContractMetrics.CommencementOverdue.Add(flagged);
        logger.LogWarning("Hire commencement watch: {Count} appointment(s) overdue (deadline before {Deadline:yyyy-MM-dd}).", flagged, deadline);
        return flagged;
    }
}

/// <summary>FUN-029 (J): daily commencement watch, leader-elected (one replica per database).</summary>
public sealed class HireCommencementWatchJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<HireCommencementOptions> options,
    ILogger<HireCommencementWatchJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Jobs.CommencementWatchEnabled)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
                    await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                        db,
                        AdvisoryLockKeys.HireCommencementWatch,
                        ct => scope.ServiceProvider.GetRequiredService<HireCommencementWatcher>()
                            .FlagOverdueAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hire commencement watch cycle failed.");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Jobs.CommencementWatchIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
