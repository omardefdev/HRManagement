using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Infrastructure.Services;

/// <summary>PLAN-46 (section 1, rule 7): what the sweep of disciplinary approvals reads and applies.</summary>
public sealed class DisciplinaryApprovalStore(ComplianceDbContext db, DisciplinaryApprovalService approvals) : IWorkflowApprovalOwnerStore
{
    public string EntityType => DisciplinaryApprovalService.EntityType;

    public DbContext LeaderDatabase => db;

    public long LeaderLockKey => AdvisoryLockKeys.DisciplinaryApprovalSweep;

    /// <summary>The audit log is outside row security, and every saved action has a row in it from the same save.</summary>
    public async Task<IReadOnlyList<Guid>> ListTenantsAsync(CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == nameof(DisciplinaryAction) && l.TenantId != Guid.Empty)
            .Select(l => l.TenantId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(
        DateTime submittedBefore, WorkflowApprovalPendingRecord? after, int take, CancellationToken ct)
    {
        var query = db.DisciplinaryActions.AsNoTracking()
            .Where(a => a.ApprovalStatus == DisciplinaryApprovalStatus.PendingApproval
                && a.WorkflowInstanceId != null
                && a.SubmittedAt != null
                && a.SubmittedAt <= submittedBefore);
        if (after is not null)
            query = query.Where(a => a.SubmittedAt > after.SubmittedAt || (a.SubmittedAt == after.SubmittedAt && a.Id.CompareTo(after.EntityId) > 0));

        return await query
            .OrderBy(a => a.SubmittedAt)
            .ThenBy(a => a.Id)
            .Take(take)
            .Select(a => new WorkflowApprovalPendingRecord(a.Id, a.WorkflowInstanceId!.Value, a.SubmittedAt!.Value))
            .ToListAsync(ct);
    }

    public Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct) => approvals.ApplyOutcomeAsync(outcome, ct);
}
