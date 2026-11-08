using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Infrastructure.Services;

/// <summary>PLAN-46 (section 1, rule 7): what the sweep of card approvals reads and applies.</summary>
public sealed class CardRequestApprovalStore(IDCardDbContext db, CardRequestApprovalService approvals) : IWorkflowApprovalOwnerStore
{
    public string EntityType => CardRequestApprovalService.EntityType;

    public DbContext LeaderDatabase => db;

    public long LeaderLockKey => AdvisoryLockKeys.CardRequestApprovalSweep;

    /// <summary>The audit log is outside row security, and every saved request has a row in it from the same save.</summary>
    public async Task<IReadOnlyList<Guid>> ListTenantsAsync(CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == nameof(CardRequest) && l.TenantId != Guid.Empty)
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(
        DateTime submittedBefore, WorkflowApprovalPendingRecord? after, int take, CancellationToken ct)
    {
        var query = db.CardRequests.AsNoTracking()
            .Where(r => r.Status == CardRequestStatus.PendingApproval
                && !r.IsCancelled
                && r.WorkflowInstanceId != null
                && r.SubmittedAt != null
                && r.SubmittedAt <= submittedBefore);
        if (after is not null)
            query = query.Where(r => r.SubmittedAt > after.SubmittedAt || (r.SubmittedAt == after.SubmittedAt && r.Id.CompareTo(after.EntityId) > 0));

        return await query
            .OrderBy(r => r.SubmittedAt)
            .ThenBy(r => r.Id)
            .Take(take)
            .Select(r => new WorkflowApprovalPendingRecord(r.Id, r.WorkflowInstanceId!.Value, r.SubmittedAt!.Value))
            .ToListAsync(ct);
    }

    public Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct) => approvals.ApplyOutcomeAsync(outcome, ct);
}
