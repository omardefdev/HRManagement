using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>PLAN-58 (§3): what a four-eyes request carries besides its target. Never an account number.</summary>
public sealed record PayrollDualControlPayload(
    string Reason,
    decimal? Amount = null,
    string? Reference = null,
    decimal? OriginalAmount = null,
    decimal? PaidAmount = null,
    string? TargetStatus = null)
{
    public static PayrollDualControlPayload Read(string json) =>
        JsonSerializer.Deserialize<PayrollDualControlPayload>(json) ?? new PayrollDualControlPayload(string.Empty);

    public string Write() => JsonSerializer.Serialize(this);
}

/// <summary>PLAN-58 (P-4): the policy that asks for each kind of request, and the one that decides it.</summary>
public static class PayrollDualControlKinds
{
    /// <summary>
    /// Who asks: an approver asks to release a hold or resolve a critical alert; the processor asks to adjust an arrears row; the poster asks
    /// to reissue a bank file and to record the channel's settlement or rejection.
    /// </summary>
    public static string RequestPolicy(PayrollDualControlKind kind) => kind switch
    {
        PayrollDualControlKind.HoldRelease or PayrollDualControlKind.CriticalAlertResolve => HrManagementPolicies.PayrollApprove,
        PayrollDualControlKind.ArrearsAdjust => HrManagementPolicies.PayrollProcess,
        _ => HrManagementPolicies.PayrollPost,
    };

    /// <summary>Who decides: a holder of <c>Payroll.Approve</c> who is not the requester (D-PAY-19 for the reissue, §7 for the channel).</summary>
    public static string DecisionPolicy(PayrollDualControlKind kind) => HrManagementPolicies.PayrollApprove;
}

/// <summary>PLAN-58 (§3): the Arabic messages of the four-eyes requests.</summary>
public static class PayrollDualControlMessages
{
    public const string RequestNotFound = "طلب الأربع عيون غير موجود.";
    public const string ReasonRequired = "السبب مطلوب.";
    public const string RejectionNoteRequired = "ملاحظة الرفض مطلوبة.";
    public const string PendingExists = "يوجد طلب معلّق من النوع نفسه على السجل نفسه؛ يُقرَّر أولاً.";
    public const string DeciderIsRequester = "لا يقرّر الطالبُ طلبه: يقرّره شخص آخر من جلسته.";
    public const string NotYoursToDecide = "لا تملك صلاحية القرار في هذا النوع من الطلبات.";
    public const string Expired = "انتهت مهلة الطلب قبل أن يُقرَّر؛ أُغلق بلا أثر. يُقدَّم طلب جديد إن لزم.";
    public const string ChangedMeanwhile = "تغيّر الطلب أثناء القرار (قرّره شخص آخر)؛ لم يُنفَّذ شيء.";
    public const string HoldNotFound = "حجز الصرف غير موجود.";
    public const string HoldNotActive = "حجز الصرف ليس نشطاً.";
    public const string AlertNotFound = "تنبيه الاحتيال غير موجود.";
    public const string AlertNotCritical = "التنبيه غير الحرج يُحل بفعل واحد من صفحته؛ طلب الأربع عيون للتنبيه الحرج.";
    public const string AlertNotOpen = "التنبيه مغلق من قبل.";
    public const string AlertTargetStatus = "حالة الحل يجب أن تكون «محلول» أو «مرفوض».";
    public const string ArrearsNotFound = "قيد المتأخرات غير موجود.";
    public const string ArrearsVoided = "قيد المتأخرات أُلغي بعكس تشغيله؛ لا يُعدَّل.";
    public const string ArrearsAmounts = "المبلغان غير صالحين: المدفوع لا يتجاوز الأصلي، ولا مبلغ سالباً.";
    public const string FileNotFound = "ملف الصرف غير موجود.";
    public const string FileSuperseded = "هذا الملف أُبطل بإعادة إصدار سابقة؛ يُعاد إصدار الملف الساري فقط.";
    public const string SettlementReferenceRequired = "مرجع كشف المصرف مطلوب.";
    public const string SettlementAmountRequired = "المبلغ المسدَّد مطلوب.";
    public const string RegimeStillBlocked =
        "ما زال HR.Core يحجب هذا الموظف بنظام خدمته؛ لا يُفرج عن حجز حاجز نظام الخدمة حتى يُرفع الحاجز من شؤون الموظفين.";
    public const string RegimeClientMissing = "تعذر التحقق من نظام خدمة الموظف لدى HR.Core؛ لم يُفرج عن الحجز.";
}

/// <summary>A pending request as the inbox lists it.</summary>
public sealed record PayrollDualControlInboxItem(
    Guid Id,
    PayrollDualControlKind Kind,
    Guid TargetId,
    PayrollDualControlPayload Payload,
    Guid RequestedBy,
    DateTime RequestedAt,
    DateTime ExpiresAt);

/// <summary>
/// PLAN-58 (§3, §7, P-7, G-PAY-09): «four eyes» as a request and a decision by two people. The actor of each comes from the token (the
/// controller passes it); the decider is not the requester, holds the kind's decision policy (and, through it, no conflicting pair of
/// roles), and the act happens inside the decision, in one transaction with it. A request not decided before its expiry
/// (<c>Payroll:DualControl:ExpiryHours</c>) ends <c>Expired</c> with no effect.
/// </summary>
public sealed class PayrollDualControlService(
    IPayrollRepository repository,
    IMediator mediator,
    PaymentHoldService holds,
    IOptions<PayrollDualControlOptions> options,
    IPaymentBatchFileWriter? files = null,
    IHrCoreServiceRegimeClient? regimeClient = null,
    TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<RunActResult> RequestAsync(
        PayrollDualControlKind kind,
        Guid targetId,
        PayrollDualControlPayload payload,
        Guid requestedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Reason))
            return RunActResult.Invalid(PayrollDualControlMessages.ReasonRequired);

        repository.ClearChangeTracker();
        var (tenantId, refusal) = await TargetAsync(kind, targetId, payload, cancellationToken);
        if (refusal is not null)
            return refusal;

        var now = Now;
        var request = new PayrollDualControlRequest
        {
            TenantId = tenantId,
            Kind = kind,
            TargetId = targetId,
            PayloadJson = (payload with { Reason = payload.Reason.Trim() }).Write(),
            RequestedBy = requestedBy,
            RequestedAt = now,
            ExpiresAt = now.AddHours(Math.Max(1, options.Value.ExpiryHours)),
        };

        // An expired request on the same target no longer stands in the way.
        foreach (var stale in await repository.PayrollDualControlRequests
                     .Where(r => r.Kind == kind && r.TargetId == targetId && r.Status == PayrollDualControlStatus.Pending)
                     .ToListAsync(cancellationToken))
        {
            if (!stale.IsExpired(now))
                return RunActResult.Conflict(PayrollDualControlMessages.PendingExists);
            stale.Expire(now);
        }

        await repository.AddAsync(request, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PayrollDualControlMessages.PendingExists);
        }

        return RunActResult.Done(request.Id);
    }

    /// <summary>
    /// The decision. <paramref name="deciderHoldsTheKindsPolicy"/> is the caller's authorization for the kind (403 when false); the rest
    /// of the rules are the service's: the decider is not the requester (400), the request is pending (409) and not expired (400),
    /// a rejection has a note (400). An approval executes the act; if the act refuses, nothing changes.
    /// </summary>
    public async Task<RunActResult> DecideAsync(
        Guid requestId,
        bool approve,
        string? notes,
        Guid decidedBy,
        Func<PayrollDualControlKind, Task<bool>> deciderHoldsTheKindsPolicy,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var request = await repository.PayrollDualControlRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
            return RunActResult.NotFound(PayrollDualControlMessages.RequestNotFound);
        if (request.Status != PayrollDualControlStatus.Pending)
            return RunActResult.Conflict(PayrollDualControlRequest.AlreadyDecided);

        var now = Now;
        if (request.IsExpired(now))
        {
            request.Expire(now);
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RunActResult.Invalid(PayrollDualControlMessages.Expired);
        }

        if (request.RequestedBy == decidedBy)
            return RunActResult.Invalid(PayrollDualControlMessages.DeciderIsRequester);
        if (!await deciderHoldsTheKindsPolicy(request.Kind))
            return RunActResult.Forbidden(PayrollDualControlMessages.NotYoursToDecide);
        if (!approve && string.IsNullOrWhiteSpace(notes))
            return RunActResult.Invalid(PayrollDualControlMessages.RejectionNoteRequired);

        if (approve)
        {
            var effect = await ApplyAsync(request, PayrollDualControlPayload.Read(request.PayloadJson), decidedBy, cancellationToken);
            if (!effect.Success)
            {
                repository.ClearChangeTracker();
                return effect;
            }
        }

        // The act's own saves may have cleared nothing: the request is the tracked row read above.
        request.Decide(approve, decidedBy, now, notes);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PayrollDualControlMessages.ChangedMeanwhile);
        }

        return RunActResult.Done(request.Id);
    }

    /// <summary>
    /// The inbox: the pending requests the caller may decide, without the ones he asked for himself; requests past their expiry are closed
    /// (with no effect) on the way.
    /// </summary>
    public async Task<IReadOnlyList<PayrollDualControlInboxItem>> InboxAsync(
        Guid userId, Func<PayrollDualControlKind, Task<bool>> mayDecide, CancellationToken cancellationToken)
    {
        await ExpireDueAsync(cancellationToken);
        var kinds = new List<PayrollDualControlKind>();
        foreach (var kind in Enum.GetValues<PayrollDualControlKind>())
        {
            if (await mayDecide(kind))
                kinds.Add(kind);
        }

        var pending = await repository.PayrollDualControlRequests.AsNoTracking()
            .Where(r => r.Status == PayrollDualControlStatus.Pending && r.RequestedBy != userId && kinds.Contains(r.Kind))
            .OrderBy(r => r.RequestedAt)
            .Take(500)
            .ToListAsync(cancellationToken);
        return pending
            .Select(r => new PayrollDualControlInboxItem(r.Id, r.Kind, r.TargetId, PayrollDualControlPayload.Read(r.PayloadJson), r.RequestedBy, r.RequestedAt, r.ExpiresAt))
            .ToList();
    }

    /// <summary>Closes every pending request past its expiry, with no effect. Returns how many.</summary>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var now = Now;
        var due = await repository.PayrollDualControlRequests
            .Where(r => r.Status == PayrollDualControlStatus.Pending && r.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        foreach (var request in due)
            request.Expire(now);
        if (due.Count > 0)
        {
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request decided or expired one of them meanwhile: nothing to add.
            }

            repository.ClearChangeTracker();
        }

        return due.Count;
    }

    /// <summary>The target of a request, and its state for the kind: the agency it belongs to, or the refusal.</summary>
    private async Task<(Guid TenantId, RunActResult? Refusal)> TargetAsync(
        PayrollDualControlKind kind, Guid targetId, PayrollDualControlPayload payload, CancellationToken ct)
    {
        switch (kind)
        {
            case PayrollDualControlKind.HoldRelease:
                {
                    var hold = await repository.PaymentHolds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == targetId, ct);
                    if (hold is null) return (Guid.Empty, RunActResult.NotFound(PayrollDualControlMessages.HoldNotFound));
                    return hold.Status != PaymentHoldStatus.Active
                        ? (hold.TenantId, RunActResult.Invalid(PayrollDualControlMessages.HoldNotActive))
                        : (hold.TenantId, null);
                }

            case PayrollDualControlKind.CriticalAlertResolve:
                {
                    var alert = await repository.FraudAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetId, ct);
                    if (alert is null) return (Guid.Empty, RunActResult.NotFound(PayrollDualControlMessages.AlertNotFound));
                    if (alert.Severity != FraudAlertSeverity.Critical) return (alert.TenantId, RunActResult.Invalid(PayrollDualControlMessages.AlertNotCritical));
                    if (alert.Status is not (FraudAlertStatus.Open or FraudAlertStatus.UnderReview))
                        return (alert.TenantId, RunActResult.Invalid(PayrollDualControlMessages.AlertNotOpen));
                    return payload.TargetStatus is null or FraudAlertStatus.Resolved or FraudAlertStatus.Dismissed
                        ? (alert.TenantId, null)
                        : (alert.TenantId, RunActResult.Invalid(PayrollDualControlMessages.AlertTargetStatus));
                }

            case PayrollDualControlKind.ArrearsAdjust:
                {
                    var entry = await repository.ArrearsLedgerEntries.AsNoTracking().FirstOrDefaultAsync(a => a.Id == targetId, ct);
                    if (entry is null) return (Guid.Empty, RunActResult.NotFound(PayrollDualControlMessages.ArrearsNotFound));
                    if (entry.Status == ArrearsLedgerStatus.Voided) return (entry.TenantId, RunActResult.Invalid(PayrollDualControlMessages.ArrearsVoided));
                    return payload is { OriginalAmount: >= 0, PaidAmount: >= 0 } && payload.PaidAmount <= payload.OriginalAmount
                        ? (entry.TenantId, null)
                        : (entry.TenantId, RunActResult.Invalid(PayrollDualControlMessages.ArrearsAmounts));
                }

            case PayrollDualControlKind.BankFileReissue:
                {
                    var file = await repository.PaymentBatchFiles.AsNoTracking()
                        .Where(f => f.Id == targetId).Select(f => new { f.TenantId, f.SupersededById }).FirstOrDefaultAsync(ct);
                    if (file is null) return (Guid.Empty, RunActResult.NotFound(PayrollDualControlMessages.FileNotFound));
                    return file.SupersededById is null
                        ? (file.TenantId, null)
                        : (file.TenantId, RunActResult.Invalid(PayrollDualControlMessages.FileSuperseded));
                }

            case PayrollDualControlKind.BatchSettle:
                {
                    var batch = await repository.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == targetId, ct);
                    if (batch is null) return (Guid.Empty, RunActResult.NotFound(PaymentBatchMessages.BatchNotFound));
                    if (string.IsNullOrWhiteSpace(payload.Reference)) return (batch.TenantId, RunActResult.Invalid(PayrollDualControlMessages.SettlementReferenceRequired));
                    if (payload.Amount is null) return (batch.TenantId, RunActResult.Invalid(PayrollDualControlMessages.SettlementAmountRequired));
                    var acknowledged = await repository.PaymentOrders.AsNoTracking()
                        .Where(o => o.PaymentBatchId == batch.Id && o.Status == PaymentOrderStatus.Acknowledged)
                        .Select(o => o.Amount).ToListAsync(ct);
                    if (batch.Status is not (PaymentBatchStatus.Acked or PaymentBatchStatus.Settled) || acknowledged.Count == 0)
                        return (batch.TenantId, RunActResult.Invalid(PaymentBatchMessages.NothingToSettle));
                    return acknowledged.Sum() == payload.Amount
                        ? (batch.TenantId, null)
                        : (batch.TenantId, RunActResult.Invalid(PaymentBatchMessages.SettledAmountMismatch));
                }

            case PayrollDualControlKind.BatchReject:
                {
                    var batch = await repository.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == targetId, ct);
                    if (batch is null) return (Guid.Empty, RunActResult.NotFound(PaymentBatchMessages.BatchNotFound));
                    var sent = await repository.PaymentOrders.AsNoTracking().AnyAsync(
                        o => o.PaymentBatchId == batch.Id && (o.Status == PaymentOrderStatus.Submitted || o.Status == PaymentOrderStatus.Acknowledged), ct);
                    return sent ? (batch.TenantId, null) : (batch.TenantId, RunActResult.Invalid(PaymentBatchMessages.NothingToReject));
                }

            default:
                return (Guid.Empty, RunActResult.Invalid(PayrollDualControlMessages.RequestNotFound));
        }
    }

    /// <summary>The act an approval executes, inside the decision's transaction. The requester and the decider are the two people.</summary>
    private async Task<RunActResult> ApplyAsync(
        PayrollDualControlRequest request, PayrollDualControlPayload payload, Guid decidedBy, CancellationToken ct)
    {
        switch (request.Kind)
        {
            case PayrollDualControlKind.HoldRelease:
                {
                    var hold = await repository.PaymentHolds.FirstOrDefaultAsync(h => h.Id == request.TargetId, ct);
                    if (hold is null) return RunActResult.NotFound(PayrollDualControlMessages.HoldNotFound);
                    if (hold.Status != PaymentHoldStatus.Active) return RunActResult.Invalid(PayrollDualControlMessages.HoldNotActive);
                    if (hold.HoldType == PaymentHoldType.RegimeBlocker && await RegimeRefusalAsync(hold.EmployeeId, ct) is { } blocked)
                        return blocked;
                    try
                    {
                        holds.ReleaseHold(hold, request.RequestedBy, decidedBy, payload.Reason);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        return RunActResult.Invalid(ex.Message);
                    }

                    if (hold.FraudAlertId is Guid alertId)
                    {
                        var alert = await repository.FraudAlerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
                        if (alert is not null && alert.Status is FraudAlertStatus.Open or FraudAlertStatus.UnderReview)
                        {
                            alert.Status = FraudAlertStatus.Resolved;
                            alert.ResolvedBy = request.RequestedBy;
                            alert.ApprovedBy = decidedBy;
                            alert.ResolutionReason = payload.Reason;
                            alert.UpdatedAt = DateTime.UtcNow;
                        }
                    }

                    return RunActResult.Done(hold.Id);
                }

            case PayrollDualControlKind.CriticalAlertResolve:
                {
                    var alert = await repository.FraudAlerts.FirstOrDefaultAsync(a => a.Id == request.TargetId, ct);
                    if (alert is null) return RunActResult.NotFound(PayrollDualControlMessages.AlertNotFound);
                    if (alert.Status is not (FraudAlertStatus.Open or FraudAlertStatus.UnderReview))
                        return RunActResult.Invalid(PayrollDualControlMessages.AlertNotOpen);
                    var linked = alert.PaymentHoldId is Guid holdId
                        ? await repository.PaymentHolds.FirstOrDefaultAsync(h => h.Id == holdId && h.Status == PaymentHoldStatus.Active, ct)
                        : null;
                    try
                    {
                        holds.ResolveAlert(alert, linked, request.RequestedBy, decidedBy, payload.Reason,
                            payload.TargetStatus ?? FraudAlertStatus.Resolved, releaseHold: linked is not null);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        return RunActResult.Invalid(ex.Message);
                    }

                    return RunActResult.Done(alert.Id);
                }

            case PayrollDualControlKind.ArrearsAdjust:
                {
                    var entry = await repository.ArrearsLedgerEntries.FirstOrDefaultAsync(a => a.Id == request.TargetId, ct);
                    if (entry is null) return RunActResult.NotFound(PayrollDualControlMessages.ArrearsNotFound);
                    return entry.TryManualUpdate(payload.OriginalAmount ?? -1m, payload.PaidAmount ?? -1m, request.RequestedBy, decidedBy, payload.Reason)
                        ? RunActResult.Done(entry.Id)
                        : RunActResult.Invalid(entry.Status == ArrearsLedgerStatus.Voided
                            ? PayrollDualControlMessages.ArrearsVoided
                            : PayrollDualControlMessages.ArrearsAmounts);
                }

            case PayrollDualControlKind.BankFileReissue:
                {
                    if (files is null) return RunActResult.Invalid(PayrollDualControlMessages.FileNotFound);
                    var (fileId, error) = await files.ReissueAsync(request.TargetId, payload.Reason, request.RequestedBy, decidedBy, ct);
                    return error is null ? RunActResult.Done(fileId) : RunActResult.Invalid(error);
                }

            case PayrollDualControlKind.BatchSettle:
                // §7: the settlement is recorded by this decision only (Pay058SinglePaymentPathArchitectureTests).
                return await mediator.Send(new SettlePaymentBatchCommand(request.TargetId, payload.Reference ?? string.Empty, payload.Amount ?? -1m), ct);

            case PayrollDualControlKind.BatchReject:
                return await mediator.Send(new RejectPaymentBatchCommand(request.TargetId, payload.Reason), ct);

            default:
                return RunActResult.Invalid(PayrollDualControlMessages.RequestNotFound);
        }
    }

    /// <summary>
    /// §3 rule 2: a service-regime blocker's hold is released only while HR.Core answers «not blocked» for the employee at the moment of
    /// the decision. Without a client, nothing is released.
    /// </summary>
    private async Task<RunActResult?> RegimeRefusalAsync(Guid employeeId, CancellationToken ct)
    {
        if (regimeClient is null)
            return RunActResult.Invalid(PayrollDualControlMessages.RegimeClientMissing);
        var answer = await regimeClient.GetPayrollBlockersAsync([employeeId], ct);
        return answer.Any(b => b.EmployeeId == employeeId && b.Blocked)
            ? RunActResult.Invalid(PayrollDualControlMessages.RegimeStillBlocked)
            : null;
    }
}
