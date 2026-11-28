using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

/// <summary>The submission a record enters <see cref="SeparationStatus.PendingApproval"/> with: never without an engine instance.</summary>
public sealed record SeparationSubmission(Guid InstanceId, string WorkflowCode, int SubmissionNo);

/// <summary>A refused transition, in Arabic: the person reads it as it is.</summary>
public sealed class SeparationTransitionException(string message) : InvalidOperationException(message);

/// <summary>
/// PLAN-60 (§1, G-SEP-04): the separation record's state machine. <see cref="Transition"/> is the one place a status changes (with
/// <see cref="ReconcileFromLegacyFlags"/>, the backfill's rules for a record written before the state machine). Every other rule of the
/// journey reads the status.
/// </summary>
public static class SeparationWorkflowRules
{
    public const string ExecutingCancelMessage = "إلغاء فصل قيد التنفيذ يتطلب مسار التعويض (PLAN-63)";
    public const string NoInstanceMessage = "لا يُقدَّم سجل الفصل للاعتماد دون نسخة اعتماد في محرك سير العمل.";
    public const string CancelReasonRequiredMessage = "سبب الإلغاء إلزامي.";
    public const string AlreadyCancelledMessage = "سجل الفصل ملغى.";
    public const string NoEmployeeReason = "PLAN-60: سجل بلا موظف — لا يمكن اعتماده";
    public const string DuplicateReason = "PLAN-60: سجل مكرر";

    private static readonly IReadOnlyDictionary<SeparationStatus, SeparationStatus[]> Allowed =
        new Dictionary<SeparationStatus, SeparationStatus[]>
        {
            [SeparationStatus.Draft] = [SeparationStatus.PendingApproval, SeparationStatus.Cancelled],
            [SeparationStatus.PendingApproval] =
                [SeparationStatus.Approved, SeparationStatus.Rejected, SeparationStatus.Draft, SeparationStatus.Cancelled],
            [SeparationStatus.Rejected] = [SeparationStatus.Draft, SeparationStatus.Cancelled],
            [SeparationStatus.Approved] = [SeparationStatus.Executing, SeparationStatus.Cancelled],
            [SeparationStatus.Executing] = [SeparationStatus.AwaitingIntervention, SeparationStatus.Completed],
            [SeparationStatus.AwaitingIntervention] = [SeparationStatus.Executing],
            [SeparationStatus.Completed] = [],
            [SeparationStatus.Cancelled] = []
        };

    /// <summary>The statuses that hold the employee: at most one record of an employee is in one of them (the partial unique index).</summary>
    public static IReadOnlyList<SeparationStatus> OpenStatuses { get; } =
    [
        SeparationStatus.Draft,
        SeparationStatus.PendingApproval,
        SeparationStatus.Approved,
        SeparationStatus.Executing,
        SeparationStatus.AwaitingIntervention
    ];

    /// <summary>The statuses this plan may cancel. Anything after the execution needs PLAN-63's compensation.</summary>
    public static IReadOnlyList<SeparationStatus> CancellableStatuses { get; } =
        [SeparationStatus.Draft, SeparationStatus.PendingApproval, SeparationStatus.Approved];

    public static bool IsOpen(SeparationStatus status) => OpenStatuses.Contains(status);

    public static bool IsCancellable(SeparationStatus status) => CancellableStatuses.Contains(status);

    /// <summary>The saga started: the parties were told.</summary>
    public static bool IsExecutedOrLater(SeparationStatus status) =>
        status is SeparationStatus.Executing or SeparationStatus.AwaitingIntervention or SeparationStatus.Completed;

    public static bool CanTransition(SeparationStatus from, SeparationStatus to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    public static IReadOnlyList<SeparationStatus> NextStatuses(SeparationStatus from) =>
        Allowed.TryGetValue(from, out var next) ? next : [];

    /// <summary>Why <paramref name="from"/> cannot go to <paramref name="to"/>; null when it can.</summary>
    public static string? RefusalFor(SeparationStatus from, SeparationStatus to)
    {
        if (CanTransition(from, to))
            return null;
        if (to == SeparationStatus.Cancelled)
            return from == SeparationStatus.Cancelled ? AlreadyCancelledMessage : ExecutingCancelMessage;
        return $"لا يمكن نقل سجل الفصل من «{DisplayName(from)}» إلى «{DisplayName(to)}».";
    }

    /// <summary>
    /// The one transition of a record's status. It refuses what the table refuses, <see cref="SeparationStatus.PendingApproval"/> without an
    /// engine instance, and a cancellation without a reason; and it writes what the new status means: the submitter, the decider, the
    /// executor, the completion, the cancellation. <see cref="SeparationRecord.SeparationEventPublished"/> follows the status.
    /// </summary>
    public static void Transition(
        SeparationRecord record,
        SeparationStatus to,
        Guid? actor,
        string? reason = null,
        SeparationSubmission? submission = null,
        DateTime? at = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (RefusalFor(record.Status, to) is { } refusal)
            throw new SeparationTransitionException(refusal);

        var now = at ?? DateTime.UtcNow;
        switch (to)
        {
            case SeparationStatus.PendingApproval:
                if (submission is null || submission.InstanceId == Guid.Empty)
                    throw new SeparationTransitionException(NoInstanceMessage);
                record.WorkflowInstanceId = submission.InstanceId;
                record.WorkflowCode = submission.WorkflowCode;
                record.SubmissionNo = submission.SubmissionNo;
                record.SubmittedBy = actor;
                record.SubmittedAt = now;
                record.DecidedBy = null;
                record.DecidedAt = null;
                record.RejectionReason = null;
                record.IsLegacyDraft = false;
                break;
            case SeparationStatus.Approved:
                record.DecidedBy = actor;
                record.DecidedAt = now;
                break;
            case SeparationStatus.Rejected:
                record.DecidedBy = actor;
                record.DecidedAt = now;
                record.RejectionReason = Trim(reason, 1000);
                break;
            case SeparationStatus.Executing when record.Status == SeparationStatus.Approved:
                record.ExecutedBy = actor;
                record.ExecutedAt = now;
                break;
            case SeparationStatus.Completed:
                record.CompletedAt = now;
                break;
            case SeparationStatus.Cancelled:
                if (string.IsNullOrWhiteSpace(reason))
                    throw new SeparationTransitionException(CancelReasonRequiredMessage);
                record.IsCancelled = true;
                record.CancelledAt = now;
                record.CancellationReason = Trim(reason, 500);
                break;
        }

        record.Status = to;
        record.SeparationEventPublished = record.SeparationEventPublished || IsExecutedOrLater(to);
    }

    /// <summary>
    /// PLAN-60 (P-5, rules 1 to 4), as the migration applies them in SQL: the status of a record written before the state machine, from
    /// its two flags and its saga's state; a draft is marked legacy. The seed writes its records this way (P-13).
    /// </summary>
    public static void ApplyLegacyBackfill(SeparationRecord record, bool wasPublished, string? sagaState)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.SeparationEventPublished = wasPublished;
        var (status, reason) = LegacyStatus(record.IsCancelled, record.SeparationEventPublished, record.EmployeeId, sagaState);
        SetLegacy(record, status, reason);
    }

    /// <summary>
    /// PLAN-60 (P-5, after a rollback): the previous tag moves only the old flags. A record whose flags and status disagree — cancelled
    /// while open, published while not yet executed, or open with no employee — is put back in line by the backfill's rules; any other
    /// record is left as it is. Returns whether the record changed. <c>SeparationStatusReconciler</c> runs it at start-up.
    /// </summary>
    public static bool ReconcileFromLegacyFlags(SeparationRecord record, string? sagaState)
    {
        ArgumentNullException.ThrowIfNull(record);
        var disagrees =
            (record.IsCancelled && record.Status != SeparationStatus.Cancelled)
            || (record.SeparationEventPublished && !IsExecutedOrLater(record.Status) && record.Status != SeparationStatus.Cancelled)
            || (record.EmployeeId == Guid.Empty && record.Status == SeparationStatus.Draft);
        if (!disagrees)
            return false;

        var (status, reason) = LegacyStatus(record.IsCancelled, record.SeparationEventPublished, record.EmployeeId, sagaState);
        SetLegacy(record, status, reason);
        return true;
    }

    private static void SetLegacy(SeparationRecord record, SeparationStatus status, string? reason)
    {
        record.Status = status;
        record.IsLegacyDraft = status == SeparationStatus.Draft;
        if (status == SeparationStatus.Cancelled && !record.IsCancelled)
        {
            record.IsCancelled = true;
            record.CancelledAt ??= DateTime.UtcNow;
            record.CancellationReason ??= reason;
        }

        record.SeparationEventPublished = record.SeparationEventPublished || IsExecutedOrLater(status);
    }

    /// <summary>Rules 1 to 4 of the backfill, as a function of the old flags.</summary>
    public static (SeparationStatus Status, string? Reason) LegacyStatus(
        bool isCancelled, bool published, Guid employeeId, string? sagaState)
    {
        if (isCancelled)
            return (SeparationStatus.Cancelled, null);
        if (published)
        {
            var fromSaga = sagaState switch
            {
                "Completed" => SeparationStatus.Completed,
                "AwaitingIntervention" => SeparationStatus.AwaitingIntervention,
                "Cancelled" => SeparationStatus.Cancelled,
                _ => SeparationStatus.Executing
            };
            return (fromSaga, fromSaga == SeparationStatus.Cancelled ? "إلغاء رحلة الفصل" : null);
        }

        return employeeId == Guid.Empty ? (SeparationStatus.Cancelled, NoEmployeeReason) : (SeparationStatus.Draft, null);
    }

    /// <summary>D-SEP-6: the effective date is reached when it is on or before <paramref name="today"/>, Damascus's date (the caller's).</summary>
    public static bool IsEffectiveDateReached(SeparationRecord record, DateOnly today) => record.EffectiveDate <= today;

    /// <summary>The pre-execution items still open: neither completed nor excepted (PLAN-60 §4, rule 1).</summary>
    public static IReadOnlyList<SeparationChecklist> GetOpenPreExecutionSteps(IEnumerable<SeparationChecklist> steps)
    {
        var list = steps.ToList();
        var open = list.Where(s => s.IsRequired
                                   && SeparationChecklistDefaults.IsPreExecution(s.Step)
                                   && !s.IsCompleted
                                   && !s.IsExcepted)
                       .ToList();
        // Fail closed: a pre-execution item the record lacks (never seeded) counts as open.
        open.AddRange(SeparationChecklistDefaults.DefaultSteps
            .Where(step => SeparationChecklistDefaults.IsPreExecution(step) && list.All(s => s.Step != step))
            .Select(step => new SeparationChecklist { Step = step, IsRequired = true }));
        return open.OrderBy(s => s.Step).ToList();
    }

    /// <summary>PLAN-60 (§4, rule 4): every mandatory party acknowledged.</summary>
    public static bool AreAllPartiesAcknowledged(IEnumerable<SeparationChecklist> steps)
    {
        var parties = steps.Where(s => s.IsRequired && s.CompletionMode == SeparationStepCompletionMode.PartyAck).ToList();
        return parties.Count > 0 && parties.All(s => s.IsCompleted);
    }

    public static IReadOnlyList<SeparationChecklist> GetIncompleteRequiredSteps(IEnumerable<SeparationChecklist> steps) =>
        steps.Where(s => s.IsRequired && !s.IsCompleted && !s.IsExcepted)
             .OrderBy(s => s.Step)
             .ToList();

    public static string DisplayName(SeparationStatus status) => Display(typeof(SeparationStatus), status.ToString());

    public static string DisplayName(SeparationStep step) => Display(typeof(SeparationStep), step.ToString());

    private static string Display(Type type, string name) =>
        type.GetField(name)?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? name;

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
