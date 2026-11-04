using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Movements.Infrastructure.Messaging;

/// <summary>
/// PLAN-40 (§6, D-40-4): replays the promotions and delegations whose event was lost before the fix. Only requests with a
/// committed execution record and no ledger row qualify; the consumers deduplicate by the deterministic message id.
/// Reads and publishes inside the caller's tenant (the query filter and the outbound tenant header).
/// </summary>
public sealed class MovementsMessagingRepair(MovementsDbContext db, IPublishEndpoint publish)
{
    public const string PromotionsKind = "promotions";
    public const string DelegationsKind = "delegations";

    public async Task<MessagingRepairResult> ReplayPromotionsAsync(bool dryRun, Guid? actor, CancellationToken ct = default)
    {
        var ledger = PublishedEventLedger.EntityType<IPromotionExecuted>();
        var lost = await db.PromotionRequests
            .Where(p => p.Status == PromotionRequestStatus.Executed
                        && db.MovementExecutionRecords.Any(r => r.PromotionRequestId == p.Id)
                        && !db.AuditLogs.Any(a => a.EntityType == ledger && a.EntityId == p.Id))
            .OrderBy(p => p.Id)
            .ToListAsync(ct);

        var ids = lost.Select(p => p.Id).ToList();
        if (dryRun || lost.Count == 0)
            return new MessagingRepairResult(PromotionsKind, dryRun, lost.Count, ids);

        await OutboxUnitOfWork.ExecuteAsync(db, async token =>
        {
            foreach (var promotion in lost)
                await MovementExecutedEvents.PublishPromotionExecutedAsync(db, publish, promotion, actor, PublishedEventLedger.Replayed, token);
            db.AuditLogs.Add(PublishedEventLedger.ReplayRun(PromotionsKind, lost.Count, actor));
        }, ct);
        return new MessagingRepairResult(PromotionsKind, false, lost.Count, ids);
    }

    public async Task<MessagingRepairResult> ReplayDelegationsAsync(bool dryRun, Guid? actor, CancellationToken ct = default)
    {
        var ledger = PublishedEventLedger.EntityType<IDelegationExecuted>();
        var lost = await db.DelegationRequests
            .Where(d => db.MovementExecutionRecords.Any(r => r.DelegationRequestId == d.Id)
                        && !db.AuditLogs.Any(a => a.EntityType == ledger && a.EntityId == d.Id))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var ids = lost.Select(d => d.Id).ToList();
        if (dryRun || lost.Count == 0)
            return new MessagingRepairResult(DelegationsKind, dryRun, lost.Count, ids);

        await OutboxUnitOfWork.ExecuteAsync(db, async token =>
        {
            foreach (var delegation in lost)
                await MovementExecutedEvents.PublishDelegationExecutedAsync(db, publish, delegation, actor, PublishedEventLedger.Replayed, token);
            db.AuditLogs.Add(PublishedEventLedger.ReplayRun(DelegationsKind, lost.Count, actor));
        }, ct);
        return new MessagingRepairResult(DelegationsKind, false, lost.Count, ids);
    }
}
