using HR.Contracts.Application.Features.SeparationRecords;
using HR.Contracts.Application.Services;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Workflow;

/// <summary>
/// PLAN-60 (§2, P-7): what the sweep of separation approvals reads and applies — the records waiting in <c>PendingApproval</c> since
/// before <c>WorkflowApproval:Sweep:StaleAfterMinutes</c>, and the same <c>ApplyOutcomeAsync</c> the consumer runs.
/// </summary>
public sealed class SeparationApprovalOwnerStore(ContractsDbContext db, SeparationApprovalService approvals) : IWorkflowApprovalOwnerStore
{
    public string EntityType => SeparationApprovalRouting.EntityType;

    public DbContext LeaderDatabase => db;

    public long LeaderLockKey => AdvisoryLockKeys.SeparationApprovalSweep;

    /// <summary>The audit log is outside row security, and every saved record has a row in it from the same save.</summary>
    public async Task<IReadOnlyList<Guid>> ListTenantsAsync(CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == nameof(SeparationRecord) && l.TenantId != Guid.Empty)
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(
        DateTime submittedBefore, WorkflowApprovalPendingRecord? after, int take, CancellationToken ct)
    {
        var query = db.SeparationRecords.AsNoTracking()
            .Where(r => r.Status == SeparationStatus.PendingApproval
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
