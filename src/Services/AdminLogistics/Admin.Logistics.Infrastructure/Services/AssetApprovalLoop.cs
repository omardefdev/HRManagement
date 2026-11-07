using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.Logistics.Infrastructure.Services;

public enum AssetApprovalAdvance
{
    /// <summary>The request is linked to its running instance.</summary>
    Pending,

    /// <summary>The instance had already finished, and its outcome was applied.</summary>
    Finished,

    /// <summary>HR.Core did not start it: the request keeps waiting, with the error recorded.</summary>
    Waiting,

    /// <summary>Someone moved the request while the start ran.</summary>
    Conflict,

    /// <summary>The request no longer waits for a start.</summary>
    NotStarting
}

public enum AssetWithdrawOutcome
{
    Withdrawn,

    /// <summary>The approval finished first, so its outcome was applied instead.</summary>
    Approved,

    /// <summary>The approval finished first, so its outcome was applied instead.</summary>
    Rejected,

    Conflict,

    /// <summary>HR.Core could not be reached: nothing changed here, and the instance still runs.</summary>
    Failed,

    NotWaiting,

    NotFound
}

/// <summary>
/// PLAN-45 (G-WF-02, G-IDA-02): the life of a request between its first save and its outcome. The request is saved in
/// <c>ApprovalStarting</c> before HR.Core is called, so a failure there loses nothing: the error is recorded, and the same key finds the
/// same instance later, which closes PLAN-43's M6. Two writers meet on <c>xmin</c>: the one that loses saves nothing and looks again.
/// </summary>
public sealed class AssetApprovalLoop(
    AdminLogisticsDbContext db,
    IAssetApprovalStarter starter,
    IEmployeeLookup employees,
    IOptionsMonitor<AssetApprovalOptions> options,
    ILogger<AssetApprovalLoop> logger)
{
    public const string CancelledElsewhereReason = "أُلغي مسار الاعتماد قبل ربطه بالطلب.";
    public const string WithdrawnMeanwhileReason = "سُحب الطلب أثناء بدء الاعتماد.";
    public const string UnknownRequesterError = "لا يُعرف طالب الإجراء، فلا يمكن بدء الاعتماد آلياً.";

    private const string SettlementSource = "HR.Core";

    private static readonly string InProgress = nameof(WorkflowStatus.InProgress);
    private static readonly string Completed = nameof(WorkflowStatus.Completed);
    private static readonly string Rejected = nameof(WorkflowStatus.Rejected);
    private static readonly string Cancelled = nameof(WorkflowStatus.Cancelled);

    private int MaxAttempts => Math.Max(1, options.CurrentValue.Retry.MaxAttempts);

    /// <summary>Starts the approval of a saved request, or finds the one a lost answer left behind, and saves the result.</summary>
    public async Task<AssetApprovalAdvance> AdvanceAsync(AssetAssignment assignment, CancellationToken ct = default)
    {
        if (assignment.Status != AssetAssignmentStatus.ApprovalStarting)
            return AssetApprovalAdvance.NotStarting;

        var asset = assignment.Asset ?? await db.Assets.FirstAsync(a => a.Id == assignment.AssetId, ct);
        var started = await starter.StartAsync(assignment, asset, ct);
        if (!started.Success)
            return await RecordFailureAsync(assignment, started.Error, ct);

        var result = await ApplyStartedAsync(assignment, asset, started.InstanceId!.Value, started.InstanceStatus, ct);
        return result is null
            ? await RecordFailureAsync(assignment, started.InstanceStatus, ct)
            : await SaveAsync(assignment, started.InstanceId.Value, result.Value, IsWithdrawnAssignmentAsync, ct);
    }

    /// <inheritdoc cref="AdvanceAsync(AssetAssignment, CancellationToken)"/>
    public async Task<AssetApprovalAdvance> AdvanceAsync(AssetLossReport report, CancellationToken ct = default)
    {
        if (report.Status != AssetLossReportStatus.ApprovalStarting)
            return AssetApprovalAdvance.NotStarting;

        var asset = report.Asset ?? await db.Assets.FirstAsync(a => a.Id == report.AssetId, ct);
        var started = await starter.StartAsync(report, asset, await HolderOfAsync(report.AssetId, ct), ct);
        if (!started.Success)
            return await RecordFailureAsync(report, started.Error, ct);

        var result = await ApplyStartedAsync(report, asset, started.InstanceId!.Value, started.InstanceStatus, ct);
        return result is null
            ? await RecordFailureAsync(report, started.InstanceStatus, ct)
            : await SaveAsync(report, started.InstanceId.Value, result.Value, IsWithdrawnLossAsync, ct);
    }

    /// <summary>A request nobody can start for: it goes straight to a person.</summary>
    public async Task MarkUnstartableAsync(IAssetApprovalRequest request, CancellationToken ct = default)
    {
        AssetApprovalRules.RecordStartFailure(request, UnknownRequesterError, DateTime.UtcNow, maxAttempts: 1);
        await TrySaveAsync(ct);
    }

    /// <summary>
    /// HR.Core first: a request is withdrawn here only once its instance can no longer decide anything. If the instance finished first,
    /// its outcome is applied instead. A lost race is looked at once more, because the other writer may have linked the instance.
    /// </summary>
    public async Task<AssetWithdrawOutcome> WithdrawAssignmentAsync(
        Guid assignmentId, Guid? actor, string reason, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var assignment = await db.AssetAssignments.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
            if (assignment is null)
                return AssetWithdrawOutcome.NotFound;
            if (!AssetApprovalRules.IsWaiting(assignment.Status))
                return AssetWithdrawOutcome.NotWaiting;

            var decision = await CloseInstanceAsync(assignment, () => starter.StartAsync(assignment, assignment.Asset, ct), reason, ct);
            switch (decision)
            {
                case AssetWithdrawOutcome.Withdrawn:
                    AssetApprovalRules.Withdraw(assignment, actor, reason, DateTime.UtcNow);
                    break;
                case AssetWithdrawOutcome.Approved:
                    await ApplyApprovedAsync(EnsureLinked(assignment), assignment.Asset, ct);
                    break;
                case AssetWithdrawOutcome.Rejected:
                    AssetApprovalRules.ApplyRejected(EnsureLinked(assignment), null, DateTime.UtcNow);
                    break;
                default:
                    db.ChangeTracker.Clear();
                    return decision;
            }

            if (await TrySaveAsync(ct))
                return decision;
            if (attempt > 0)
                return AssetWithdrawOutcome.Conflict;
        }
    }

    /// <inheritdoc cref="WithdrawAssignmentAsync(Guid, Guid?, string, CancellationToken)"/>
    public async Task<AssetWithdrawOutcome> WithdrawLossAsync(
        Guid reportId, Guid? actor, string reason, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            var report = await db.AssetLossReports.Include(r => r.Asset).FirstOrDefaultAsync(r => r.Id == reportId, ct);
            if (report is null)
                return AssetWithdrawOutcome.NotFound;
            if (!AssetApprovalRules.IsWaiting(report.Status))
                return AssetWithdrawOutcome.NotWaiting;

            var holder = await HolderOfAsync(report.AssetId, ct);
            var decision = await CloseInstanceAsync(report, () => starter.StartAsync(report, report.Asset, holder, ct), reason, ct);
            switch (decision)
            {
                case AssetWithdrawOutcome.Withdrawn:
                    AssetApprovalRules.Withdraw(report, actor, reason, DateTime.UtcNow);
                    break;
                case AssetWithdrawOutcome.Approved:
                    await ApplyApprovedAsync(EnsureLinked(report), report.Asset, ct);
                    break;
                case AssetWithdrawOutcome.Rejected:
                    AssetApprovalRules.ApplyRejected(EnsureLinked(report), null);
                    break;
                default:
                    db.ChangeTracker.Clear();
                    return decision;
            }

            if (await TrySaveAsync(ct))
                return decision;
            if (attempt > 0)
                return AssetWithdrawOutcome.Conflict;
        }
    }

    /// <summary>What the withdrawal may do, once HR.Core has answered for the instance.</summary>
    private async Task<AssetWithdrawOutcome> CloseInstanceAsync(
        IAssetApprovalRequest request, Func<Task<AssetApprovalStartResult>> findInstance, string reason, CancellationToken ct)
    {
        if (request.WorkflowInstanceId is null)
        {
            // A start whose answer was lost may have committed, and the key finds it. With HR.Core down the asset is still freed: an
            // instance left behind can only reach a request that no outcome changes any more.
            var found = await findInstance();
            if (!found.Success)
            {
                logger.LogWarning("Withdrawing {RequestId} with no instance: {Error}", request.Id, found.Error);
                return AssetWithdrawOutcome.Withdrawn;
            }

            request.WorkflowInstanceId = found.InstanceId;
            if (found.InstanceStatus != InProgress)
                return FromFinished(found.InstanceStatus);
        }

        var cancelled = await starter.CancelAsync(request.WorkflowInstanceId!.Value, reason, ct);
        return cancelled.Outcome switch
        {
            AssetApprovalCancelOutcome.Cancelled => AssetWithdrawOutcome.Withdrawn,
            AssetApprovalCancelOutcome.Finished => FromFinished(cancelled.InstanceStatus),
            AssetApprovalCancelOutcome.Conflict => AssetWithdrawOutcome.Conflict,
            _ => AssetWithdrawOutcome.Failed
        };
    }

    private static AssetWithdrawOutcome FromFinished(string? status) =>
        status == Completed ? AssetWithdrawOutcome.Approved
        : status == Rejected ? AssetWithdrawOutcome.Rejected
        : status == Cancelled ? AssetWithdrawOutcome.Withdrawn
        : AssetWithdrawOutcome.Failed;

    /// <summary>A request linked after its instance finished goes through <c>PendingApproval</c> in memory, and the outcome is the one save.</summary>
    private static AssetAssignment EnsureLinked(AssetAssignment assignment)
    {
        if (assignment.Status == AssetAssignmentStatus.ApprovalStarting)
            AssetApprovalRules.LinkPending(assignment, assignment.WorkflowInstanceId!.Value);
        return assignment;
    }

    private static AssetLossReport EnsureLinked(AssetLossReport report)
    {
        if (report.Status == AssetLossReportStatus.ApprovalStarting)
            AssetApprovalRules.LinkPending(report, report.WorkflowInstanceId!.Value);
        return report;
    }

    private async Task<AssetApprovalAdvance?> ApplyStartedAsync(
        AssetAssignment assignment, Asset asset, Guid instanceId, string? status, CancellationToken ct)
    {
        if (status == InProgress)
        {
            AssetApprovalRules.LinkPending(assignment, instanceId);
            return AssetApprovalAdvance.Pending;
        }

        if (status == Completed)
        {
            AssetApprovalRules.LinkPending(assignment, instanceId);
            await ApplyApprovedAsync(assignment, asset, ct);
            return AssetApprovalAdvance.Finished;
        }

        if (status == Rejected)
        {
            AssetApprovalRules.LinkPending(assignment, instanceId);
            AssetApprovalRules.ApplyRejected(assignment, null, DateTime.UtcNow);
            return AssetApprovalAdvance.Finished;
        }

        if (status == Cancelled)
        {
            assignment.WorkflowInstanceId = instanceId;
            AssetApprovalRules.Withdraw(assignment, null, CancelledElsewhereReason, DateTime.UtcNow);
            return AssetApprovalAdvance.Finished;
        }

        return null;
    }

    private async Task<AssetApprovalAdvance?> ApplyStartedAsync(
        AssetLossReport report, Asset asset, Guid instanceId, string? status, CancellationToken ct)
    {
        if (status == InProgress)
        {
            AssetApprovalRules.LinkPending(report, instanceId);
            return AssetApprovalAdvance.Pending;
        }

        if (status == Completed)
        {
            AssetApprovalRules.LinkPending(report, instanceId);
            await ApplyApprovedAsync(report, asset, ct);
            return AssetApprovalAdvance.Finished;
        }

        if (status == Rejected)
        {
            AssetApprovalRules.LinkPending(report, instanceId);
            AssetApprovalRules.ApplyRejected(report, null);
            return AssetApprovalAdvance.Finished;
        }

        if (status == Cancelled)
        {
            report.WorkflowInstanceId = instanceId;
            AssetApprovalRules.Withdraw(report, null, CancelledElsewhereReason, DateTime.UtcNow);
            return AssetApprovalAdvance.Finished;
        }

        return null;
    }

    private async Task ApplyApprovedAsync(AssetAssignment assignment, Asset asset, CancellationToken ct)
    {
        var others = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == assignment.AssetId && a.ReturnedAt == null && a.Id != assignment.Id)
            .ToListAsync(ct);
        AssetApprovalRules.ApplyApproved(assignment, asset, others);
    }

    private async Task ApplyApprovedAsync(AssetLossReport report, Asset asset, CancellationToken ct)
    {
        var open = await db.AssetAssignments
            .Where(a => a.AssetId == report.AssetId && a.ReturnedAt == null)
            .ToListAsync(ct);

        string? holderName = null;
        if (open.FirstOrDefault()?.EmployeeId is Guid holder && holder != Guid.Empty)
            (await employees.GetEmployeeDisplayNamesAsync([holder], ct)).TryGetValue(holder, out holderName);

        AssetLostRules.ApplyLost(asset, open, report, holderName);
        report.SettlementApprovedAt = DateTime.UtcNow;
        report.SettlementApprovedBy = SettlementSource;
    }

    private async Task<Guid?> HolderOfAsync(Guid assetId, CancellationToken ct) =>
        await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == assetId && a.ReturnedAt == null)
            .Select(a => (Guid?)a.EmployeeId)
            .FirstOrDefaultAsync(ct);

    private async Task<AssetApprovalAdvance> RecordFailureAsync(IAssetApprovalRequest request, string? error, CancellationToken ct)
    {
        if (AssetApprovalRules.RecordStartFailure(request, error, DateTime.UtcNow, MaxAttempts))
            logger.LogError("Approval start for {RequestId} gave up after {Attempts} attempts: {Error}", request.Id, request.ApprovalStartAttempts, error);

        return await TrySaveAsync(ct) ? AssetApprovalAdvance.Waiting : AssetApprovalAdvance.Conflict;
    }

    private async Task<AssetApprovalAdvance> SaveAsync(
        IAssetApprovalRequest request,
        Guid instanceId,
        AssetApprovalAdvance result,
        Func<Guid, CancellationToken, Task<bool>> isWithdrawn,
        CancellationToken ct)
    {
        if (await TrySaveAsync(ct))
            return result;

        // Withdrawn while the start ran: nobody else knows this instance, and it must not reach the inbox.
        if (await isWithdrawn(request.Id, ct))
            await starter.CancelAsync(instanceId, WithdrawnMeanwhileReason, ct);
        return AssetApprovalAdvance.Conflict;
    }

    private Task<bool> IsWithdrawnAssignmentAsync(Guid id, CancellationToken ct) =>
        db.AssetAssignments.AsNoTracking().AnyAsync(a => a.Id == id && a.Status == AssetAssignmentStatus.Withdrawn, ct);

    private Task<bool> IsWithdrawnLossAsync(Guid id, CancellationToken ct) =>
        db.AssetLossReports.AsNoTracking().AnyAsync(r => r.Id == id && r.Status == AssetLossReportStatus.Withdrawn, ct);

    /// <summary>False when another writer moved a row first. Nothing of this attempt stays tracked.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ConcurrencyExtensions.IsDeadlock(ex))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
