using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollRuns.Reversal;

/// <summary>
/// PLAN-56 (G-PAY-06): a reversal of a posted run is a request with a reason. Whoever holds <c>Payroll.Reverse</c> asks (the poster
/// may ask for the reversal of what he posted); another person decides (<see cref="DecidePayrollReversalCommand"/>). One request
/// waits on a run at a time.
/// </summary>
public record RequestPayrollReversalCommand(Guid PayrollRunId, Guid RequestedBy, string? Reason) : IRequest<RunActResult>;

/// <summary>
/// PLAN-56 (G-PAY-06, D-PAY-16): the decision on a reversal request, by a holder of <c>Payroll.Approve</c> who is neither the
/// requester nor the person who posted the run. A rejection needs its note and changes nothing but the request. An approval, in one
/// transaction and one save: voids every payment batch of the run that has not left (and cancels its draft orders), refuses the
/// whole decision if any batch or order has left (D-PAY-16: a correction run is the way then), releases the run's inputs, reverses
/// the run, and stages <c>IPayrollReversed</c> in the outbox. No reversal batch is written: nothing was paid, so nothing is recovered.
/// </summary>
public record DecidePayrollReversalCommand(Guid RequestId, Guid DecidedBy, bool Approve, string? Notes) : IRequest<RunActResult>;

public static class PayrollReversalMessages
{
    public const string ReasonRequired = "سبب العكس مطلوب.";
    public const string RequestNotFound = "طلب العكس غير موجود.";
    public const string PendingRequestExists = "لهذا التشغيل طلب عكس بانتظار القرار.";
    public const string RejectionNoteRequired = "ملاحظة رفض طلب العكس مطلوبة.";
    public const string DeciderIsRequester = "لا يقرر في طلب العكس من طلبه: يقرر فيه شخص آخر.";
    public const string DeciderIsPoster = "لا يقرر في عكس التشغيل من رحّله: يقرر فيه شخص آخر.";
    public const string ReleaseReason = "عكس التشغيل بطلب معتمد";

    /// <summary>
    /// A correction of the run still stands (it is neither cancelled nor reversed): reversing the run under it would leave a
    /// correction of nothing, paid or recovered on top of a run that paid nothing.
    /// </summary>
    public const string HasCorrection =
        "لا يُعكس تشغيل له تشغيل تصحيح قائم. ألغِ تشغيل التصحيح إن لم يُرحَّل، أو اعكسه بطلب معتمد إن رُحّل، ثم اطلب عكس هذا التشغيل.";

    /// <summary>
    /// A later run already took back this correction's negative difference: reversing the correction would free its adjustment
    /// for a second correction, and the difference would be recovered twice.
    /// </summary>
    public const string RecoveryApplied =
        "لا يُعكس تشغيل تصحيح استُرد فرقه السالب في تشغيل لاحق. صحّح الفرق بتسوية موجّهة جديدة إلى التشغيل المرجعي.";

    /// <summary>
    /// D-PAY-16: the run's money has left for the bank (a batch was sent, acknowledged, settled or returned, or an order left). The
    /// run is not reversed; what was paid wrongly is corrected by a correction run, whose negative difference becomes a recovery.
    /// </summary>
    public const string MoneyHasLeft =
        "لا يُعكس تشغيل خرجت دفعته إلى المصرف (أُرسلت أو سُدّدت). صحّح ما صُرف بتشغيل تصحيح: أدخل تسوية موجّهة إلى هذا التشغيل " +
        "واعتمدها ثم أنشئ تشغيل التصحيح؛ الفرق السالب يُسترد خصماً في الدورة التالية.";
}

/// <summary>What a decision reads of the run's payment side, and whether the run's money has left (D-PAY-16).</summary>
internal static class PayrollReversalPayments
{
    /// <summary>The run's disbursement batches that a reversal acts on. A batch voided or reversed before is no longer one.</summary>
    public static bool IsLive(PaymentBatch batch) =>
        batch.BatchKind == PaymentBatchKind.Disbursement
        && batch.Status is not (PaymentBatchStatus.Voided or PaymentBatchStatus.Reversed);

    /// <summary>
    /// True when any live disbursement batch of the run has left for the channel, or any payment order of the run has: the run is
    /// not reversed then.
    /// </summary>
    public static bool MoneyHasLeft(IEnumerable<PaymentBatch> batches, IEnumerable<PaymentOrder> orders) =>
        batches.Where(IsLive).Any(b => !PaymentBatchTransitions.HasNotLeft(b.Status))
        || orders.Any(o => PaymentOrderTransitions.HasLeft(o.Status));
}

public class RequestPayrollReversalHandler(IPayrollRepository repository, IPayrollCalendar? calendar = null)
    : IRequestHandler<RequestPayrollReversalCommand, RunActResult>
{
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<RunActResult> Handle(RequestPayrollReversalCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return RunActResult.Invalid(PayrollReversalMessages.ReasonRequired);

        repository.ClearChangeTracker();
        var run = await repository.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.PayrollRunId, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (run.ValidateCanReverse() is { } notPosted)
            return RunActResult.Invalid(notPosted);
        if (await PayrollRunLifecycle.HasFundingOrPartialDisbursementAsync(repository, run, cancellationToken))
            return RunActResult.Invalid(RunLifecycleMessages.HasFundingOrPartialDisbursement);

        // D-PAY-16 is the decision's rule, not the request's: where the run's money stands is read when a second person approves
        // (a batch may leave, or come back rejected, between the two). The request records that someone asked, and why.
        if (await repository.PayrollReversalRequests.AnyAsync(
                r => r.PayrollRunId == run.Id && r.Status == PayrollReversalRequestStatus.Pending, cancellationToken))
            return RunActResult.Conflict(PayrollReversalMessages.PendingRequestExists);

        var reason = request.Reason.Trim();
        var reversal = new PayrollReversalRequest
        {
            TenantId = run.TenantId,
            PayrollRunId = run.Id,
            RequestedBy = request.RequestedBy,
            RequestedAt = _calendar.UtcNow,
            Reason = reason.Length <= PayrollReversalRequest.ReasonMaxLength ? reason : reason[..PayrollReversalRequest.ReasonMaxLength],
        };
        await repository.AddAsync(reversal, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ConcurrencyExtensions.TryGetUniqueConstraintName(ex, out var constraint)
                ? constraint == PayrollRunIndexNames.OnePendingReversalRequest
                : ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Two requests at the same moment: the unique index lets one in.
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PayrollReversalMessages.PendingRequestExists);
        }

        return RunActResult.Done(reversal.Id);
    }
}

public class DecidePayrollReversalHandler(
    IPayrollRepository repository,
    IPayrollEventPublisher eventPublisher,
    IPayrollCalendar? calendar = null)
    : IRequestHandler<DecidePayrollReversalCommand, RunActResult>
{
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<RunActResult> Handle(DecidePayrollReversalCommand request, CancellationToken cancellationToken)
    {
        if (!request.Approve && string.IsNullOrWhiteSpace(request.Notes))
            return RunActResult.Invalid(PayrollReversalMessages.RejectionNoteRequired);

        repository.ClearChangeTracker();
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var reversal = await repository.PayrollReversalRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (reversal is null)
            return RunActResult.NotFound(PayrollReversalMessages.RequestNotFound);
        if (reversal.Status != PayrollReversalRequestStatus.Pending)
            return RunActResult.Conflict(PayrollReversalRequest.AlreadyDecided);

        var run = await repository.GetPayrollRunAsync(reversal.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);

        // The separation of duties: the decider is another person than the requester, and than whoever posted the run. It holds
        // for a rejection as for an approval: the decision is not the requester's nor the poster's to make.
        if (request.DecidedBy == reversal.RequestedBy)
            return RunActResult.Invalid(PayrollReversalMessages.DeciderIsRequester);
        if (run.KnownPoster is Guid poster && poster == request.DecidedBy)
            return RunActResult.Invalid(PayrollReversalMessages.DeciderIsPoster);

        var now = _calendar.UtcNow;
        if (!request.Approve)
        {
            // A rejection changes the request and nothing else: not the run, not a batch, not an input.
            reversal.Decide(approve: false, request.DecidedBy, now, request.Notes);
            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RunActResult.Done(reversal.Id);
        }

        if (run.ValidateCanReverse() is { } notPosted)
            return RunActResult.Invalid(notPosted);
        if (await PayrollRunLifecycle.HasFundingOrPartialDisbursementAsync(repository, run, cancellationToken))
            return RunActResult.Invalid(RunLifecycleMessages.HasFundingOrPartialDisbursement);

        // A correction of this run that still stands, and a correction whose recovery a later run already took: neither is
        // reversed (see the messages). Nothing changes.
        if (await repository.PayrollRuns.AsNoTracking().AnyAsync(
                r => r.SourceRunId == run.Id && r.Status != PayrollRunStatus.Cancelled && r.Status != PayrollRunStatus.Reversed,
                cancellationToken))
            return RunActResult.Invalid(PayrollReversalMessages.HasCorrection);
        if (await CorrectionRecoveryGate.HasAppliedRecoveryAsync(repository, run, cancellationToken))
            return RunActResult.Invalid(PayrollReversalMessages.RecoveryApplied);

        // D-PAY-16: the line between money that has left and money that has not. Any batch or order that left refuses the whole
        // decision; nothing changes, and the message points to a correction run.
        var batches = await repository.PaymentBatches.Where(b => b.PayrollRunId == run.Id).ToListAsync(cancellationToken);
        var orders = await repository.PaymentOrders.Where(o => o.PayrollRunId == run.Id).ToListAsync(cancellationToken);
        if (PayrollReversalPayments.MoneyHasLeft(batches, orders))
            return RunActResult.Invalid(PayrollReversalMessages.MoneyHasLeft);

        // 1. Every batch that has not left is voided, through the batch's own transitions; its orders that never left are cancelled.
        //    An order the channel rejected stays as the channel's record.
        var voided = new HashSet<Guid>();
        foreach (var batch in batches.Where(PayrollReversalPayments.IsLive))
        {
            if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Voided))
                return RunActResult.Invalid(PayrollReversalMessages.MoneyHasLeft);
            batch.Status = PaymentBatchStatus.Voided;
            voided.Add(batch.Id);
        }

        foreach (var order in orders.Where(o => voided.Contains(o.PaymentBatchId)
                                                && PaymentOrderTransitions.CanTransition(o.Status, PaymentOrderStatus.Cancelled)))
            order.Status = PaymentOrderStatus.Cancelled;

        // 2. What the run's calculation consumed is free again (in this transaction).
        await PayrollInputRelease.ReleaseAsync(repository, run, PayrollReversalMessages.ReleaseReason, request.DecidedBy, cancellationToken);

        // 3. The run and the request.
        var reversedNetPay = run.TotalNetPay;
        run.Reverse();
        reversal.Decide(approve: true, request.DecidedBy, now, request.Notes);

        // 4. The event, staged in the outbox before the one save that writes the reversal (PLAN-40, SingleSave).
        await eventPublisher.PublishPayrollReversedAsync(
            run.Id, run.PeriodStart, run.PeriodEnd, reversedNetPay, reversal.Id, now, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return RunActResult.Done(reversal.Id);
    }
}
