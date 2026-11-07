using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

/// <summary>
/// PLAN-45 (G-WF-02, G-IDA-02): the moves of a request for approval. A request is saved in <c>ApprovalStarting</c>, and
/// <see cref="LinkPending(AssetAssignment, Guid)"/> is the only way into <c>PendingApproval</c>, so no request waits for an approval
/// that does not exist. <c>Wf045NoPendingWithoutInstanceArchitectureTests</c> keeps it so.
/// </summary>
public static class AssetApprovalRules
{
    public const int MinWithdrawReasonLength = 10;
    public const int MaxWithdrawReasonLength = 500;
    public const int MaxStartErrorLength = 500;

    private const int MaxRecordedReasonLength = 256;

    /// <summary>Degraded fallback: privileged classification is persisted from policy Classify.</summary>
    public static bool RequiresApproval(Asset asset) => asset.IsPrivileged;

    public static bool IsWaiting(AssetAssignmentStatus status) =>
        status is AssetAssignmentStatus.ApprovalStarting or AssetAssignmentStatus.PendingApproval;

    public static bool IsWaiting(AssetLossReportStatus status) =>
        status is AssetLossReportStatus.ApprovalStarting or AssetLossReportStatus.PendingApproval;

    public static bool CanMove(AssetAssignmentStatus from, AssetAssignmentStatus to) => (from, to) switch
    {
        (AssetAssignmentStatus.ApprovalStarting, AssetAssignmentStatus.PendingApproval) => true,
        (AssetAssignmentStatus.ApprovalStarting, AssetAssignmentStatus.Withdrawn) => true,
        (AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Active) => true,
        (AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Rejected) => true,
        (AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Withdrawn) => true,
        _ => false
    };

    public static bool CanMove(AssetLossReportStatus from, AssetLossReportStatus to) => (from, to) switch
    {
        (AssetLossReportStatus.ApprovalStarting, AssetLossReportStatus.PendingApproval) => true,
        (AssetLossReportStatus.ApprovalStarting, AssetLossReportStatus.Withdrawn) => true,
        (AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Confirmed) => true,
        (AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Rejected) => true,
        (AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Withdrawn) => true,
        _ => false
    };

    public static bool IsValidWithdrawReason(string? reason) =>
        reason?.Trim() is { Length: >= MinWithdrawReasonLength and <= MaxWithdrawReasonLength };

    /// <summary>The approval exists: the status and the instance go into the same save.</summary>
    public static void LinkPending(AssetAssignment assignment, Guid instanceId)
    {
        Require(CanMove(assignment.Status, AssetAssignmentStatus.PendingApproval), assignment.Status);
        assignment.Status = AssetAssignmentStatus.PendingApproval;
        Link(assignment, instanceId);
    }

    /// <inheritdoc cref="LinkPending(AssetAssignment, Guid)"/>
    public static void LinkPending(AssetLossReport report, Guid instanceId)
    {
        Require(CanMove(report.Status, AssetLossReportStatus.PendingApproval), report.Status);
        report.Status = AssetLossReportStatus.PendingApproval;
        Link(report, instanceId);
    }

    /// <summary>One more failed start. True the first time the attempts reach <paramref name="maxAttempts"/>.</summary>
    public static bool RecordStartFailure(IAssetApprovalRequest request, string? error, DateTime now, int maxAttempts)
    {
        request.ApprovalStartAttempts++;
        request.ApprovalStartLastAttemptAt = now;
        request.ApprovalStartLastError = Bounded(error, MaxStartErrorLength);

        var reachedLimit = !request.NeedsIntervention && request.ApprovalStartAttempts >= maxAttempts;
        if (reachedLimit)
            request.NeedsIntervention = true;
        return reachedLimit;
    }

    /// <summary>The asset is free again: an assignment with <c>ReturnedAt</c> is no longer an open holder. Nothing is deleted.</summary>
    public static void Withdraw(AssetAssignment assignment, Guid? actor, string reason, DateTime now)
    {
        Require(CanMove(assignment.Status, AssetAssignmentStatus.Withdrawn), assignment.Status);
        assignment.Status = AssetAssignmentStatus.Withdrawn;
        assignment.ReturnedAt = now;
        RecordWithdrawal(assignment, actor, reason, now);
    }

    /// <inheritdoc cref="Withdraw(AssetAssignment, Guid?, string, DateTime)"/>
    public static void Withdraw(AssetLossReport report, Guid? actor, string reason, DateTime now)
    {
        Require(CanMove(report.Status, AssetLossReportStatus.Withdrawn), report.Status);
        report.Status = AssetLossReportStatus.Withdrawn;
        RecordWithdrawal(report, actor, reason, now);
    }

    /// <summary>The approved outcome of a linked assignment. The asset may have left the storage meanwhile: then it is refused.</summary>
    public static void ApplyApproved(AssetAssignment assignment, Asset asset, IEnumerable<AssetAssignment> otherOpenAssignments)
    {
        Require(CanMove(assignment.Status, AssetAssignmentStatus.Active), assignment.Status);
        if (!AssetAssignmentRules.CanAssign(asset, otherOpenAssignments))
        {
            assignment.Status = AssetAssignmentStatus.Rejected;
            assignment.ReturnedAt = DateTime.UtcNow;
            return;
        }

        assignment.Status = AssetAssignmentStatus.Active;
        asset.Status = AssetStatus.InUse;
    }

    public static void ApplyRejected(AssetAssignment assignment, string? reason, DateTime now)
    {
        Require(CanMove(assignment.Status, AssetAssignmentStatus.Rejected), assignment.Status);
        assignment.Status = AssetAssignmentStatus.Rejected;
        assignment.ReturnedAt = now;
        if (!string.IsNullOrWhiteSpace(reason))
            assignment.EvidenceRef = Bounded($"rejected: {reason}".Trim(), MaxRecordedReasonLength);
    }

    public static void ApplyRejected(AssetLossReport report, string? reason)
    {
        Require(CanMove(report.Status, AssetLossReportStatus.Rejected), report.Status);
        report.Status = AssetLossReportStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var note = $"مرفوض: {reason.Trim()}";
            report.Notes = Bounded(string.IsNullOrWhiteSpace(report.Notes) ? note : $"{report.Notes} — {note}", MaxRecordedReasonLength);
        }
    }

    private static void Link(IAssetApprovalRequest request, Guid instanceId)
    {
        if (instanceId == Guid.Empty)
            throw new ArgumentException("A pending request needs its workflow instance.", nameof(instanceId));

        request.WorkflowInstanceId = instanceId;
        request.ApprovalStartLastError = null;
        request.NeedsIntervention = false;
    }

    private static void RecordWithdrawal(IAssetApprovalRequest request, Guid? actor, string reason, DateTime now)
    {
        if (!IsValidWithdrawReason(reason))
            throw new ArgumentException($"A withdrawal needs a reason of at least {MinWithdrawReasonLength} characters.", nameof(reason));

        request.WithdrawnAt = now;
        request.WithdrawnBy = actor;
        request.WithdrawnReason = reason.Trim();
        request.NeedsIntervention = false;
    }

    private static void Require(bool allowed, Enum status)
    {
        if (!allowed)
            throw new InvalidOperationException($"This move is not allowed from {status}.");
    }

    private static string? Bounded(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
