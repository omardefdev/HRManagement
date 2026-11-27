using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayrollRuns;

/// <summary>PLAN-58: the Arabic messages of a payment batch's acts.</summary>
public static class PaymentBatchMessages
{
    public const string BatchNotFound = "دفعة الصرف غير موجودة.";
    public const string RunNotPosted = "لا يُصرف إلا لتشغيل مرحّل.";
    public const string NotADisbursement = "هذه الدفعة ليست دفعة صرف.";
    public const string AlreadySent = "أُرسلت هذه الدفعة من قبل؛ لم يُنشأ إرسال ولا ملف ثانٍ. الأوامر المُفرَج عنها بعد الإرسال تُرسل من «إرسال المُفرَج عنهم».";
    public const string NotSentYet = "لم تُرسل الدفعة بعد: يرسلها المُرحِّل من «تنفيذ الصرف» أولاً.";
    public const string NothingToSend = "لا أمر في الدفعة قابل للإرسال الآن: موظفوها كلهم عليهم حجز (حجز صرف، تنبيه حرج، حساب محال، حاجز نظام الخدمة) أو بلا حساب مصرفي مثبَّت. لم يتغير شيء.";
    public const string NothingReleased = "لا أمر محجوز رُفع حجزه منذ الإرسال؛ لم يُرسل شيء.";
    public const string ChangedMeanwhile = "تغيّرت الدفعة أثناء الطلب (عولجت أو قُرّر فيها من طلب آخر)؛ لم يُنفَّذ شيء. حدّث الصفحة.";
    public const string ExecutorIsCreator = "لا ينفذ منشئ التشغيل صرفه.";
    public const string ExecutorIsApprover = "لا ينفذ صرفَ التشغيل أحدُ معتمديه.";
    public const string ExecutorIsFundingDecider = "لا ينفذ صرفَ الدفعة من قرّر تمويلها.";
    public const string ChannelReferenceRequired = "مرجع القناة مطلوب لتسجيل الإشعار.";
    public const string NothingToAcknowledge = "لا أمر مُرسَل بانتظار إشعار القناة في هذه الدفعة.";
    public const string SettlementReferenceRequired = "مرجع كشف المصرف مطلوب لإثبات تنفيذ التحويل.";
    public const string NothingToSettle = "لا أمر مُقَرّ من القناة بانتظار التسوية في هذه الدفعة: يُسجَّل إشعار القناة أولاً.";
    public const string SettledAmountMismatch = "المبلغ المسدَّد لا يساوي مجموع الأوامر المُقرّة في الدفعة؛ رُفضت التسوية والدفعة كما هي. التسوية الجزئية لكل أمر في الموجة الثانية.";
    public const string RejectionReasonRequired = "سبب الرفض مطلوب.";
    public const string NothingToReject = "لا أمر مُرسَل أو مُقَرّ في هذه الدفعة لتسجيل رفضه.";
}

/// <summary>
/// PLAN-58 (§1, P-5): the separation of duties of a sending. The person who sends a batch is neither the run's creator, nor any of its
/// approvers (every approval of every version, not the latest), nor the person who decided its funding.
/// </summary>
internal static class PaymentBatchSod
{
    public static async Task<string?> RefusalAsync(IPayrollRepository repository, PayrollRun run, PaymentBatch batch, Guid actorId, CancellationToken ct)
    {
        if (run.CreatedBy is Guid creator && creator == actorId)
            return PaymentBatchMessages.ExecutorIsCreator;
        if (run.Approvals.Any(a => a.Decision == ApprovalDecision.Approved && a.ApproverId == actorId))
            return PaymentBatchMessages.ExecutorIsApprover;
        if (batch.FundingDecisionId is Guid decisionId
            && await repository.FundingDecisions.AsNoTracking().AnyAsync(d => d.Id == decisionId && d.ApprovedBy == actorId, ct))
            return PaymentBatchMessages.ExecutorIsFundingDecider;
        return null;
    }
}

/// <summary>
/// PLAN-58 (§1, P-5, G-PAY-08): hands a posted batch to the channel — the one entry to the paying side of a run. In one request: the fraud
/// scan, then the gate per employee (held employees' orders <c>Held</c>, the others <c>Submitted</c>), the sum rule, then the bank file
/// of this sending (§2). The executor is the poster's role (<c>Payroll.Post</c>) and is not the run's creator, any approver or the
/// funding decider. A second request for the same sending sends nothing and makes no second file (409).
/// </summary>
public record ProcessPaymentBatchCommand(Guid PaymentBatchId, Guid ActorId) : IRequest<RunActResult>;

public class ProcessPaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentDisbursementGate disbursementGate,
    IPaymentOrderCoordinator paymentOrderCoordinator,
    IMediator mediator,
    IPaymentBatchFileWriter fileWriter)
    : IRequestHandler<ProcessPaymentBatchCommand, RunActResult>
{
    public async Task<RunActResult> Handle(ProcessPaymentBatchCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);
        if (batch.BatchKind != PaymentBatchKind.Disbursement)
            return RunActResult.Invalid(PaymentBatchMessages.NotADisbursement);
        if (batch.Status is PaymentBatchStatus.Submitted or PaymentBatchStatus.Acked or PaymentBatchStatus.Settled)
            return RunActResult.Conflict(PaymentBatchMessages.AlreadySent);
        if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Submitted))
            return RunActResult.Invalid($"لا يمكن إرسال دفعة في الحالة {batch.Status}.");

        var run = await repository.GetPayrollRunAsync(batch.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null || run.Status != PayrollRunStatus.Posted)
            return RunActResult.Invalid(PaymentBatchMessages.RunNotPosted);
        if (await PaymentBatchSod.RefusalAsync(repository, run, batch, request.ActorId, cancellationToken) is { } sod)
            return RunActResult.Invalid(sod);

        // The fraud scan of this request: its alerts and holds are saved before the gate reads them.
        await mediator.Send(new Fraud.RunPayrollFraudScanCommand(run.Id), cancellationToken);

        var gate = await disbursementGate.EvaluateAsync(batch, cancellationToken);
        if (gate.Stopped)
            return RunActResult.Invalid(gate.StopReason!);

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var sent = await paymentOrderCoordinator.EnsureAndSubmitOrdersAsync(batch, gate, PaymentSending.Initial, cancellationToken);
        if (sent.Count == 0)
        {
            repository.ClearChangeTracker();
            return RunActResult.Invalid(PaymentBatchMessages.NothingToSend);
        }

        // Submitted — not paid. Settlement is recorded only when two people confirm the bank statement (§7).
        batch.Status = PaymentBatchStatus.Submitted;
        batch.SubmittedAt = DateTime.UtcNow;
        batch.UpdatedAt = batch.SubmittedAt.Value;

        var job = await repository.PayrollRunJobs
            .Where(j => j.PayrollRunId == run.Id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is not null)
        {
            job.Phase = PayrollRunJobPhase.Pay;
            job.LastCheckpointAt = DateTime.UtcNow;
        }

        return await PaymentBatchSave.CommitSendingAsync(
            repository, transaction, batch, sent, PaymentBatchFileKind.Initial, request.ActorId, fileWriter, cancellationToken);
    }
}

/// <summary>
/// PLAN-58 (§1, §6 rule 1): sends the orders of a batch already sent whose hold was lifted since (a four-eyes release, an account verified
/// and pinned), with a supplementary bank file of those orders alone. A held employee is paid after his release with no new batch. The
/// same separation of duties, fraud scan and gate as the first sending.
/// </summary>
public record SubmitHeldPaymentOrdersCommand(Guid PaymentBatchId, Guid ActorId) : IRequest<RunActResult>;

public class SubmitHeldPaymentOrdersHandler(
    IPayrollRepository repository,
    IPaymentDisbursementGate disbursementGate,
    IPaymentOrderCoordinator paymentOrderCoordinator,
    IMediator mediator,
    IPaymentBatchFileWriter fileWriter)
    : IRequestHandler<SubmitHeldPaymentOrdersCommand, RunActResult>
{
    public async Task<RunActResult> Handle(SubmitHeldPaymentOrdersCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);
        if (batch.BatchKind != PaymentBatchKind.Disbursement)
            return RunActResult.Invalid(PaymentBatchMessages.NotADisbursement);
        if (batch.Status is not (PaymentBatchStatus.Submitted or PaymentBatchStatus.Acked or PaymentBatchStatus.Settled))
            return RunActResult.Invalid(PaymentBatchMessages.NotSentYet);

        var run = await repository.GetPayrollRunAsync(batch.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null || run.Status != PayrollRunStatus.Posted)
            return RunActResult.Invalid(PaymentBatchMessages.RunNotPosted);
        if (await PaymentBatchSod.RefusalAsync(repository, run, batch, request.ActorId, cancellationToken) is { } sod)
            return RunActResult.Invalid(sod);

        await mediator.Send(new Fraud.RunPayrollFraudScanCommand(run.Id), cancellationToken);
        var gate = await disbursementGate.EvaluateAsync(batch, cancellationToken);
        if (gate.Stopped)
            return RunActResult.Invalid(gate.StopReason!);

        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var sent = await paymentOrderCoordinator.EnsureAndSubmitOrdersAsync(batch, gate, PaymentSending.HeldRelease, cancellationToken);
        if (sent.Count == 0)
        {
            repository.ClearChangeTracker();
            return RunActResult.Invalid(PaymentBatchMessages.NothingReleased);
        }

        batch.UpdatedAt = DateTime.UtcNow;
        return await PaymentBatchSave.CommitSendingAsync(
            repository, transaction, batch, sent, PaymentBatchFileKind.HeldRelease, request.ActorId, fileWriter, cancellationToken);
    }
}

/// <summary>PLAN-58: the one save of a sending: the batch, its orders and the bank file, after the sum rule, in one transaction.</summary>
internal static class PaymentBatchSave
{
    public static async Task<RunActResult> CommitSendingAsync(
        IPayrollRepository repository,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        PaymentBatch batch,
        IReadOnlyList<PaymentOrder> sent,
        PaymentBatchFileKind kind,
        Guid actorId,
        IPaymentBatchFileWriter fileWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            if (await PaymentBatchLedger.CheckAsync(repository, batch, cancellationToken) is { } broken)
            {
                repository.ClearChangeTracker();
                return RunActResult.Invalid(broken);
            }

            // Every sending has its bank file, in the sending's transaction (PLAN-58 review: the writer is required, never optional).
            var fileId = await fileWriter.WriteAsync(batch, sent, kind, actorId, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return RunActResult.Done(fileId);
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PaymentBatchMessages.ChangedMeanwhile);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PaymentBatchMessages.AlreadySent);
        }
    }

    /// <summary>The save of a confirmation (ack, settlement, rejection): the sum rule first, then one save; a lost race is a 409.</summary>
    public static async Task<RunActResult> CommitAsync(IPayrollRepository repository, PaymentBatch batch, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            if (await PaymentBatchLedger.CheckAsync(repository, batch, cancellationToken) is { } broken)
                return RunActResult.Invalid(broken);
            return RunActResult.Done(batch.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.ClearChangeTracker();
            return RunActResult.Conflict(PaymentBatchMessages.ChangedMeanwhile);
        }
    }
}

/// <summary>
/// PLAN-58 (§7): the channel acknowledged the sent orders — one act by the poster's role, with the channel's reference. Money has not
/// moved yet. A batch already acknowledged or settled whose released orders were sent since has those orders acknowledged.
/// </summary>
public record AckPaymentBatchCommand(Guid PaymentBatchId, string? ChannelReference) : IRequest<RunActResult>;

public class AckPaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentOrderCoordinator paymentOrderCoordinator)
    : IRequestHandler<AckPaymentBatchCommand, RunActResult>
{
    public async Task<RunActResult> Handle(AckPaymentBatchCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChannelReference))
            return RunActResult.Invalid(PaymentBatchMessages.ChannelReferenceRequired);

        repository.ClearChangeTracker();
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);
        if (batch.Status is not (PaymentBatchStatus.Submitted or PaymentBatchStatus.Acked or PaymentBatchStatus.Settled))
            return RunActResult.Invalid($"لا يمكن تسجيل إشعار لدفعة في الحالة {batch.Status}.");

        var reference = request.ChannelReference.Trim();
        // In its own transaction (PLAN-58 review): the sum rule is checked after the save, and a broken ledger undoes the act.
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        var acknowledged = await paymentOrderCoordinator.AcknowledgeOrdersAsync(batch.Id, reference, cancellationToken);
        if (acknowledged.Count == 0)
        {
            repository.ClearChangeTracker();
            return RunActResult.Invalid(PaymentBatchMessages.NothingToAcknowledge);
        }

        if (batch.Status == PaymentBatchStatus.Submitted)
        {
            batch.Status = PaymentBatchStatus.Acked;
            batch.AckedAt = DateTime.UtcNow;
            batch.BankTransferReference = reference;
        }

        batch.UpdatedAt = DateTime.UtcNow;
        var result = await PaymentBatchSave.CommitAsync(repository, batch, cancellationToken);
        if (result.Success)
            await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

/// <summary>
/// PLAN-58 (§7): records the settlement the bank statement confirms, for the batch's acknowledged orders together. Called from the
/// four-eyes decision (<c>PayrollDualControlService</c>, kind <c>BatchSettle</c>) only. The settled amount must be the sum of those
/// orders, or nothing changes; per-order partial settlement and matching by reference are PAY-I's.
/// </summary>
public record SettlePaymentBatchCommand(Guid PaymentBatchId, string SettlementReference, decimal SettledAmount) : IRequest<RunActResult>;

public class SettlePaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentOrderCoordinator paymentOrderCoordinator,
    IBankReconciliationCoordinator bankReconciliationCoordinator)
    : IRequestHandler<SettlePaymentBatchCommand, RunActResult>
{
    public async Task<RunActResult> Handle(SettlePaymentBatchCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SettlementReference))
            return RunActResult.Invalid(PaymentBatchMessages.SettlementReferenceRequired);

        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);
        if (batch.Status is not (PaymentBatchStatus.Acked or PaymentBatchStatus.Settled))
            return RunActResult.Invalid(PaymentBatchMessages.NothingToSettle);

        var acknowledged = await repository.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == batch.Id && o.Status == PaymentOrderStatus.Acknowledged)
            .Select(o => o.Amount)
            .ToListAsync(cancellationToken);
        if (acknowledged.Count == 0)
            return RunActResult.Invalid(PaymentBatchMessages.NothingToSettle);
        if (acknowledged.Sum() != request.SettledAmount)
            return RunActResult.Invalid(PaymentBatchMessages.SettledAmountMismatch);

        var reference = request.SettlementReference.Trim();
        await paymentOrderCoordinator.SettleOrdersAsync(batch.Id, reference, cancellationToken);
        var now = DateTime.UtcNow;
        if (batch.Status == PaymentBatchStatus.Acked)
        {
            batch.Status = PaymentBatchStatus.Settled;
            batch.DisbursedAmount = request.SettledAmount;
        }
        else
        {
            batch.DisbursedAmount += request.SettledAmount;
        }

        batch.SettledAt = now;
        batch.ProcessedAt = now;
        batch.SettlementReference = reference;
        batch.IsPartial = batch.IsPartial || batch.DisbursedAmount < batch.TotalAmount;
        batch.UpdatedAt = now;

        var saved = await PaymentBatchSave.CommitAsync(repository, batch, cancellationToken);
        if (saved.Success)
            await bankReconciliationCoordinator.ReconcileBatchAsync(batch.Id, cancellationToken);
        return saved;
    }
}

/// <summary>
/// PLAN-58 (§7): records the channel's rejection of the sent orders (or a return after settlement). Called from the four-eyes decision
/// (kind <c>BatchReject</c>) only.
/// </summary>
public record RejectPaymentBatchCommand(Guid PaymentBatchId, string Reason, bool IsReturn = false) : IRequest<RunActResult>;

public class RejectPaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentOrderCoordinator paymentOrderCoordinator)
    : IRequestHandler<RejectPaymentBatchCommand, RunActResult>
{
    public async Task<RunActResult> Handle(RejectPaymentBatchCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return RunActResult.Invalid(PaymentBatchMessages.RejectionReasonRequired);

        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null)
            return RunActResult.NotFound(PaymentBatchMessages.BatchNotFound);

        var reason = request.Reason.Trim();
        if (request.IsReturn)
        {
            if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Returned))
                return RunActResult.Invalid($"لا يمكن الانتقال من {batch.Status} إلى {PaymentBatchStatus.Returned}.");
            batch.Status = PaymentBatchStatus.Returned;
            batch.RejectionReason = reason;
            batch.DisbursedAmount = 0;
            batch.UpdatedAt = DateTime.UtcNow;
            return await PaymentBatchSave.CommitAsync(repository, batch, cancellationToken);
        }

        var rejected = await paymentOrderCoordinator.RejectOrdersAsync(batch.Id, reason, cancellationToken);
        if (rejected.Count == 0)
        {
            repository.ClearChangeTracker();
            return RunActResult.Invalid(PaymentBatchMessages.NothingToReject);
        }

        if (PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Rejected))
            batch.Status = PaymentBatchStatus.Rejected;
        batch.RejectionReason = reason;
        batch.UpdatedAt = DateTime.UtcNow;
        return await PaymentBatchSave.CommitAsync(repository, batch, cancellationToken);
    }
}
