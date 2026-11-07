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

    public async Task<IReadOnlyList<WorkflowApprovalPendingRecord>> ListPendingAsync(DateTime submittedBefore, int take, CancellationToken ct) =>
        await db.DisciplinaryActions.AsNoTracking()
            .Where(a => a.ApprovalStatus == DisciplinaryApprovalStatus.PendingApproval
                && a.WorkflowInstanceId != null
                && a.SubmittedAt <= submittedBefore)
            .OrderBy(a => a.SubmittedAt)
            .Take(take)
            .Select(a => new WorkflowApprovalPendingRecord(a.Id, a.WorkflowInstanceId!.Value))
            .ToListAsync(ct);

    public Task ApplyOutcomeAsync(IWorkflowOutcomeDecided outcome, CancellationToken ct) => approvals.ApplyOutcomeAsync(outcome, ct);
}
