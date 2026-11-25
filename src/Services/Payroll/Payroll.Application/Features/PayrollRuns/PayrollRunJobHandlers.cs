using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HRManagement.SharedKernel.SeparationOfDuties;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Features.PayrollRuns;

public record StartPayrollRunJobCommand(Guid PayrollRunId, int PartitionSize = 200)
    : IRequest<(bool Success, int LinesGenerated, Guid? JobId, string? Error)>;

public class StartPayrollRunJobHandler(
    IPayrollRepository repository,
    IPayrollEngine engine,
    IServiceScopeFactory scopeFactory,
    IOptions<PayrollBatchOptions> batchOptions,
    IPayrollNationalScopeGuard? nationalScopeGuard = null,
    IPayrollLegalControlGuard? legalControlGuard = null,
    IPayrollCalendar? calendar = null)
    : IRequestHandler<StartPayrollRunJobCommand, (bool Success, int LinesGenerated, Guid? JobId, string? Error)>
{
    /// <summary>«Today» for the interim gate: the payroll calendar's date (Damascus), never the UTC date.</summary>
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<(bool Success, int LinesGenerated, Guid? JobId, string? Error)> Handle(
        StartPayrollRunJobCommand request,
        CancellationToken cancellationToken)
    {
        var legalErr = legalControlGuard?.ValidatePayrollRelease();
        if (legalErr != null) return (false, 0, null, legalErr);

        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, 0, null, "التشغيل غير موجود.");

        var calcErr = run.ValidateCanCalculate();
        if (calcErr != null) return (false, 0, null, calcErr);

        var existing = await repository.GetActiveJobAsync(run.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == PayrollRunJobStatus.Running
                && existing.LeaseUntil.HasValue
                && existing.LeaseUntil > DateTime.UtcNow)
                return (false, 0, existing.Id, "مهمة حساب نشطة لهذا التشغيل — استخدم الإيقاف أو انتظر.");

            // PLAN-54 (§3): a resumed calculation passes the interim gate again; the exception may have expired since it started.
            var resumed = await ResolveConfigAsync(repository, run, DateOnly.FromDateTime(run.PeriodStart), cancellationToken);
            if (resumed.Config is not null
                && Features.PayrollConfigurations.PayrollInterimGate.RefusalForRun(resumed.Config, legalControlGuard, _calendar) is { } expired)
            {
                existing.Error = expired;
                existing.AppendLog(expired);
                await repository.SaveChangesAsync(cancellationToken);
                return (false, 0, existing.Id, expired);
            }

            // PLAN-56 (G-PAY-06): a calculation that goes on is a calculation: the run is «قيد الحساب» by its own method.
            run.BeginCalculating();
            existing.Status = PayrollRunJobStatus.Running;
            existing.Attempt++;
            existing.LeaseOwner = Environment.MachineName;
            existing.LeaseUntil = DateTime.UtcNow.AddMinutes(30);
            existing.Error = null;
            await repository.SaveChangesAsync(cancellationToken);
            return await PayrollRunJobProcessor.ProcessAsync(
                repository, scopeFactory, engine, batchOptions.Value, existing.Id, cancellationToken);
        }

        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);
        var coverageErr = PayrollPeriodRules.ValidatePayrollPeriodCoverage(periodStart, periodEnd);
        if (coverageErr != null) return (false, 0, null, coverageErr);

        var configResult = await ResolveConfigAsync(repository, run, periodStart, cancellationToken);
        if (configResult.Error != null) return (false, 0, null, configResult.Error);
        var config = configResult.Config!;

        // PLAN-54 (§3): a version that holds interim values is calculated only while every STR-002 exception it cites is active
        // today. The refusal is closed: the run is not pinned and not started, and a failed job row carries the reason in its log.
        if (Features.PayrollConfigurations.PayrollInterimGate.RefusalForRun(config, legalControlGuard, _calendar) is { } interimRefusal)
        {
            // One row carries the refusal however often the calculation is asked for: a job that never took a snapshot.
            var refused = await repository.PayrollRunJobs
                .Where(j => j.PayrollRunId == run.Id && j.Status == PayrollRunJobStatus.Failed && j.TotalEmployeeCount == 0)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (refused is null)
            {
                refused = new PayrollRunJob
                {
                    PayrollRunId = run.Id,
                    Phase = PayrollRunJobPhase.Snapshot,
                    Status = PayrollRunJobStatus.Failed,
                    Attempt = 0,
                    TenantId = run.TenantId
                };
                await repository.AddAsync(refused, cancellationToken);
            }

            refused.Attempt++;
            refused.Error = interimRefusal;
            refused.LastCheckpointAt = _calendar.UtcNow;
            refused.AppendLog(interimRefusal);
            await repository.SaveChangesAsync(cancellationToken);
            return (false, 0, refused.Id, interimRefusal);
        }

        run.PayrollConfigurationId = config.Id;
        run.ConfigContentHash = config.ContentHash;
        run.BeginCalculating();

        var defaults = batchOptions.Value;
        var partitionSize = request.PartitionSize < 1
            ? Math.Max(1, defaults.DefaultPartitionSize)
            : request.PartitionSize;
        var job = new PayrollRunJob
        {
            PayrollRunId = run.Id,
            Phase = PayrollRunJobPhase.Snapshot,
            Status = PayrollRunJobStatus.Running,
            Attempt = 1,
            LeaseOwner = Environment.MachineName,
            LeaseUntil = DateTime.UtcNow.AddMinutes(30),
            PartitionSize = partitionSize,
            LastCheckpointAt = DateTime.UtcNow,
            CalculateStartedAt = DateTime.UtcNow
        };
        if (Features.PayrollConfigurations.PayrollInterimGate.InterimIndicator(config) is { } interimIndicator)
            job.AppendLog(interimIndicator);
        await repository.AddAsync(job, cancellationToken);

        var profiles = await repository.EmployeePayProfiles
            .Where(p => p.IsActive)
            .Include(p => p.PayGrade)
            .AsNoTracking()
            .OrderBy(p => p.EmployeeId)
            .ToListAsync(cancellationToken);

        var scopeErr = nationalScopeGuard?.ValidatePayrollPersonCount(profiles.Count);
        if (scopeErr != null)
            return (false, 0, null, scopeErr);

        // PERF-001: bulk-load inputs in chunks and fingerprint each employee (no N+1).
        const int fingerprintChunk = 500;
        var fingerprintByEmployee = new Dictionary<Guid, string>();
        for (var offset = 0; offset < profiles.Count; offset += fingerprintChunk)
        {
            var chunkIds = profiles.Skip(offset).Take(fingerprintChunk).Select(p => p.EmployeeId).ToList();
            var bag = await PayrollPartitionInputLoader.LoadAsync(
                repository, chunkIds, run, periodStart, periodEnd, cancellationToken);
            foreach (var id in chunkIds)
            {
                fingerprintByEmployee[id] = PayrollInputFingerprint.Compute(
                    bag.AllowancesFor(id),
                    bag.DeductionsFor(id),
                    bag.AdjustmentsFor(id),
                    bag.AbsencesFor(id));
            }
        }

        // PLAN-55 (D-PAY-15): the account in force now is the one this run pays into, whatever is done to it afterwards. An employee
        // with no verified account is snapshot without one: that blocks neither the calculation nor the approval.
        var pinnedAccounts = await BankAccountPinning.ActiveAccountsAsync(repository, cancellationToken);

        var sort = 0;
        foreach (var profile in profiles)
        {
            job.InputSnapshots.Add(new PayrollRunInputSnapshot
            {
                EmployeeId = profile.EmployeeId,
                EmployeePayProfileId = profile.Id,
                BankAccountId = pinnedAccounts.TryGetValue(profile.EmployeeId, out var pinned) ? pinned : null,
                ProfileVersion = PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile),
                InputFingerprint = fingerprintByEmployee.GetValueOrDefault(profile.EmployeeId),
                PayrollConfigurationId = config.Id,
                ConfigContentHash = config.ContentHash,
                SortOrder = sort++,
                TenantId = run.TenantId
            });
        }

        job.TotalEmployeeCount = profiles.Count;

        var chunks = profiles
            .Select((p, i) => (p, i))
            .GroupBy(x => x.i / partitionSize)
            .Select((g, idx) => (Index: idx, Profiles: g.Select(x => x.p).ToList()))
            .ToList();

        foreach (var chunk in chunks)
        {
            job.Partitions.Add(new PayrollRunPartition
            {
                PartitionIndex = chunk.Index,
                Status = PayrollRunPartitionStatus.Pending,
                EmployeeCount = chunk.Profiles.Count,
                TenantId = run.TenantId
            });
        }

        job.Phase = PayrollRunJobPhase.Calculate;
        await repository.SaveChangesAsync(cancellationToken);

        return await PayrollRunJobProcessor.ProcessAsync(
            repository, scopeFactory, engine, batchOptions.Value, job.Id, cancellationToken);
    }

    internal static async Task<(PayrollConfiguration? Config, string? Error)> ResolveConfigAsync(
        IPayrollRepository repository,
        PayrollRun run,
        DateOnly periodStart,
        CancellationToken cancellationToken)
    {
        if (run.PayrollConfigurationId.HasValue)
        {
            var pinned = await repository.GetConfigurationByIdAsync(run.PayrollConfigurationId.Value, cancellationToken);
            if (pinned is null)
                return (null, "نسخة الإعدادات المثبتة على التشغيل غير موجودة.");
            if (string.IsNullOrEmpty(pinned.ContentHash))
                return (null, "نسخة الإعدادات المثبتة بلا بصمة محتوى قانونية.");
            if (!string.IsNullOrEmpty(run.ConfigContentHash)
                && !string.Equals(run.ConfigContentHash, pinned.ContentHash, StringComparison.OrdinalIgnoreCase))
                return (null, "بصمة الإعدادات المثبتة لا تطابق النسخة القانونية.");
            return (pinned, null);
        }

        var published = await repository.GetPublishedConfigurationForPeriodAsync(
            periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);
        if (published is null || string.IsNullOrEmpty(published.ContentHash))
            return (null, PayrollFrameworkMessages.NoFrameworkForPeriod);
        return (published, null);
    }
}

internal static class PayrollRunJobProcessor
{
    public static Task<(bool Success, int LinesGenerated, Guid? JobId, string? Error)> ProcessAsync(
        IPayrollRepository repository,
        IServiceScopeFactory scopeFactory,
        IPayrollEngine engine,
        PayrollBatchOptions options,
        Guid jobId,
        CancellationToken cancellationToken) =>
        PayrollRunParallelCalculator.RunAsync(repository, scopeFactory, engine, options, jobId, cancellationToken);
}

public record PausePayrollRunJobCommand(Guid JobId) : IRequest<(bool Success, string? Error)>;

public class PausePayrollRunJobHandler(IPayrollRepository repository)
    : IRequestHandler<PausePayrollRunJobCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        PausePayrollRunJobCommand request,
        CancellationToken cancellationToken)
    {
        var job = await repository.PayrollRunJobs.FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null) return (false, "المهمة غير موجودة.");
        if (job.Status != PayrollRunJobStatus.Running)
            return (false, "لا يمكن الإيقاف إلا لمهمة قيد التشغيل.");
        job.Status = PayrollRunJobStatus.Paused;
        job.LastCheckpointAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public record ResumePayrollRunJobCommand(Guid JobId)
    : IRequest<(bool Success, int LinesGenerated, Guid? JobId, string? Error)>;

public class ResumePayrollRunJobHandler(
    IPayrollRepository repository,
    IPayrollEngine engine,
    IServiceScopeFactory scopeFactory,
    IOptions<PayrollBatchOptions> batchOptions,
    IPayrollLegalControlGuard? legalControlGuard = null,
    IPayrollCalendar? calendar = null)
    : IRequestHandler<ResumePayrollRunJobCommand, (bool Success, int LinesGenerated, Guid? JobId, string? Error)>
{
    public const string NeverStarted =
        "هذه المهمة لم تبدأ الحساب: رُفض الحساب قبل بدئه. بعد زوال سبب الرفض استخدم «حساب» من صفحة التشغيل، لا «استئناف».";

    /// <summary>«Today» for the interim gate: the payroll calendar's date (Damascus), never the UTC date.</summary>
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<(bool Success, int LinesGenerated, Guid? JobId, string? Error)> Handle(
        ResumePayrollRunJobCommand request,
        CancellationToken cancellationToken)
    {
        var job = await repository.PayrollRunJobs
            .Include(j => j.Partitions)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null) return (false, 0, null, "المهمة غير موجودة.");
        if (job.Status is not (PayrollRunJobStatus.Paused or PayrollRunJobStatus.Failed))
            return (false, 0, job.Id, "لا يمكن الاستئناف إلا لمهمة متوقفة أو فاشلة.");

        // PLAN-54 (§3): a resumed calculation is a calculation. A job that never started (the row that carries a refusal: it took
        // no snapshot, so it has no partition) is not resumed, or an empty run would be marked calculated and frozen. And a job
        // that did start passes the interim gate again: an exception may have expired while it was paused.
        var run = await repository.GetPayrollRunAsync(job.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null) return (false, 0, job.Id, "التشغيل غير موجود.");
        if (job.Partitions.Count == 0)
            return (false, 0, job.Id, NeverStarted);
        if (run.ValidateCanCalculate() is { } cannotCalculate)
            return (false, 0, job.Id, cannotCalculate);
        var pinned = await StartPayrollRunJobHandler.ResolveConfigAsync(repository, run, DateOnly.FromDateTime(run.PeriodStart), cancellationToken);
        if (pinned.Error is not null)
            return (false, 0, job.Id, pinned.Error);
        if (Features.PayrollConfigurations.PayrollInterimGate.RefusalForRun(pinned.Config!, legalControlGuard, _calendar) is { } expired)
        {
            job.Error = expired;
            job.AppendLog(expired);
            await repository.SaveChangesAsync(cancellationToken);
            return (false, 0, job.Id, expired);
        }

        // PLAN-56 (G-PAY-06): a resumed calculation is a calculation: the run is «قيد الحساب» by its own method.
        run.BeginCalculating();
        job.Status = PayrollRunJobStatus.Running;
        job.Attempt++;
        job.Error = null;
        job.LeaseOwner = Environment.MachineName;
        job.LeaseUntil = DateTime.UtcNow.AddMinutes(30);
        job.ProcessedEmployeeCount = job.Partitions
            .Where(p => p.Status == PayrollRunPartitionStatus.Completed)
            .Sum(p => p.EmployeeCount);
        foreach (var p in job.Partitions.Where(p => p.Status != PayrollRunPartitionStatus.Completed))
        {
            p.Status = PayrollRunPartitionStatus.Pending;
            p.ProcessedCount = 0;
            p.CursorEmployeeId = null;
            p.WorkerId = null;
            p.Error = null;
        }
        await repository.SaveChangesAsync(cancellationToken);

        return await PayrollRunJobProcessor.ProcessAsync(
            repository, scopeFactory, engine, batchOptions.Value, job.Id, cancellationToken);
    }
}

public record CancelPayrollRunJobCommand(Guid JobId) : IRequest<(bool Success, string? Error)>;

public class CancelPayrollRunJobHandler(IPayrollRepository repository)
    : IRequestHandler<CancelPayrollRunJobCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        CancelPayrollRunJobCommand request,
        CancellationToken cancellationToken)
    {
        var job = await repository.PayrollRunJobs.FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job is null) return (false, "المهمة غير موجودة.");
        if (job.Status is PayrollRunJobStatus.Completed)
            return (false, "لا يمكن إلغاء مهمة مكتملة.");

        var run = await repository.GetPayrollRunAsync(job.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");
        if (run.Status is PayrollRunStatus.Posted or PayrollRunStatus.Reversed)
            return (false, "لا يمكن إلغاء تشغيل مرحّل.");

        job.Status = PayrollRunJobStatus.Cancelled;
        foreach (var p in await repository.PayrollRunPartitions
                     .Where(x => x.PayrollRunJobId == job.Id && x.Status != PayrollRunPartitionStatus.Completed)
                     .ToListAsync(cancellationToken))
        {
            p.Status = PayrollRunPartitionStatus.Cancelled;
            p.WorkerId = null;
        }

        run.MarkCancelled();
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public record ValidatePayrollRunCommand(Guid PayrollRunId) : IRequest<(bool Success, string? Error)>;

public class ValidatePayrollRunHandler(IPayrollRepository repository)
    : IRequestHandler<ValidatePayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ValidatePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        // PLAN-56 (G-PAY-06): the state first. A validation never brings back a run that was sent for review, approved, posted,
        // reversed or cancelled; nothing is read or written on refusal.
        if (PayrollRunTransitions.Refusal(PayrollRunAct.MarkValidated, run.Status) is { } notNow)
            return (false, notNow);

        var job = await repository.PayrollRunJobs
            .Include(j => j.Partitions)
            .Include(j => j.InputSnapshots)
            .Where(j => j.PayrollRunId == run.Id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is not null && job.Partitions.Any(p => p.Status != PayrollRunPartitionStatus.Completed))
            return (false, "هناك أقسام حساب غير مكتملة — لا يمكن التحقق.");

        // PLAN-56 (G-PAY-03): the validation's net is the engine's: PayrollNet, under the pinned framework's rounding. The total it
        // freezes is the sum of the saved lines.
        var saved = await PayrollRunLines.LoadAsync(repository, run, cancellationToken);
        var expected = job?.InputSnapshots.Count;
        var (ok, err) = PayrollRunReconciliation.ValidateTotals(run, saved.Lines, saved.ComponentsByLine, expected, round: saved.Round);
        if (!ok) return (false, err);

        var totalNet = saved.SumNet;
        run.MarkValidated(totalNet);
        if (job is not null)
        {
            job.ResultsValidated = true;
            job.Phase = PayrollRunJobPhase.Validate;
            if (job.Status != PayrollRunJobStatus.Completed)
                job.Status = PayrollRunJobStatus.Completed;
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

/// <summary>
/// Hands a posted batch to the disbursement channel. This is the only entry point to the paying
/// side of a run, so the pre-disbursement controls are enforced here.
/// </summary>
public record ProcessPaymentBatchCommand(Guid PaymentBatchId, Guid ActorId) : IRequest<(bool Success, string? Error)>;

public class ProcessPaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentDisbursementGate disbursementGate,
    IPaymentOrderCoordinator paymentOrderCoordinator,
    SeparationOfDutiesService sod,
    IMediator mediator)
    : IRequestHandler<ProcessPaymentBatchCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ProcessPaymentBatchCommand request,
        CancellationToken cancellationToken)
    {
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null) return (false, "دفعة الصرف غير موجودة.");

        if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Submitted))
            return (false, $"لا يمكن إرسال دفعة في الحالة {batch.Status}.");

        var run = await repository.GetPayrollRunAsync(batch.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null || run.Status != PayrollRunStatus.Posted)
            return (false, "التشغيل يجب أن يكون مرحّلاً قبل الصرف.");

        await mediator.Send(new Fraud.RunPayrollFraudScanCommand(run.Id), cancellationToken);

        var latestApproval = run.Approvals
            .Where(a => a.Decision == ApprovalDecision.Approved)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

        var (allowed, sodError) = PayrollSodGuard.ValidatePaymentBatchDisburse(
            sod,
            request.ActorId,
            batch.Id,
            batchCreatorId: null,
            run.CreatedBy,
            latestApproval?.ApproverId);
        if (!allowed)
            return (false, sodError);

        // Active payment holds and unresolved fraud alerts block the whole batch. These controls
        // existed but sat off the disbursement path, so a held employee was still paid.
        var block = await disbursementGate.EvaluateAsync(batch, cancellationToken);
        if (!block.Allowed)
            return (false, block.Reason);

        // Submitted — not paid. Settlement is recorded only when the channel confirms it via
        // SettlePaymentBatchCommand; there is no path from here straight to a paid state.
        batch.Status = PaymentBatchStatus.Submitted;
        batch.SubmittedAt = DateTime.UtcNow;

        var job = await repository.PayrollRunJobs
            .Where(j => j.PayrollRunId == run.Id)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is not null)
        {
            job.Phase = PayrollRunJobPhase.Pay;
            job.LastCheckpointAt = DateTime.UtcNow;
        }

        await repository.SaveChangesAsync(cancellationToken);
        await paymentOrderCoordinator.EnsureAndSubmitOrdersAsync(batch.Id, cancellationToken: cancellationToken);
        return (true, null);
    }
}

/// <summary>Records a channel acknowledgement. Money still has not moved.</summary>
public record AckPaymentBatchCommand(Guid PaymentBatchId, string? ChannelReference)
    : IRequest<(bool Success, string? Error)>;

public class AckPaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentOrderCoordinator paymentOrderCoordinator)
    : IRequestHandler<AckPaymentBatchCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        AckPaymentBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null) return (false, "دفعة الصرف غير موجودة.");
        if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Acked))
            return (false, $"لا يمكن تسجيل إشعار لدفعة في الحالة {batch.Status}.");

        batch.Status = PaymentBatchStatus.Acked;
        batch.AckedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.ChannelReference))
            batch.BankTransferReference = request.ChannelReference;

        await repository.SaveChangesAsync(cancellationToken);
        await paymentOrderCoordinator.AcknowledgeOrdersAsync(batch.Id, request.ChannelReference, cancellationToken);
        return (true, null);
    }
}

/// <summary>
/// Records confirmed settlement. Requires a settlement reference from the channel — without one
/// there is no evidence money moved, which is exactly the gap that let batches be marked paid.
/// </summary>
public record SettlePaymentBatchCommand(Guid PaymentBatchId, string SettlementReference, decimal SettledAmount)
    : IRequest<(bool Success, string? Error)>;

public class SettlePaymentBatchHandler(
    IPayrollRepository repository,
    IPaymentOrderCoordinator paymentOrderCoordinator,
    IBankReconciliationCoordinator bankReconciliationCoordinator)
    : IRequestHandler<SettlePaymentBatchCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        SettlePaymentBatchCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SettlementReference))
            return (false, "مرجع التسوية مطلوب لإثبات تنفيذ التحويل.");

        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null) return (false, "دفعة الصرف غير موجودة.");
        if (!PaymentBatchTransitions.CanTransition(batch.Status, PaymentBatchStatus.Settled))
            return (false, $"لا يمكن تسوية دفعة في الحالة {batch.Status}.");

        batch.Status = PaymentBatchStatus.Settled;
        batch.SettledAt = DateTime.UtcNow;
        batch.ProcessedAt = DateTime.UtcNow;
        batch.SettlementReference = request.SettlementReference.Trim();
        batch.DisbursedAmount = request.SettledAmount;
        batch.IsPartial = request.SettledAmount < batch.TotalAmount;

        await repository.SaveChangesAsync(cancellationToken);
        await paymentOrderCoordinator.SettleOrdersAsync(batch.Id, request.SettlementReference.Trim(), cancellationToken);
        await bankReconciliationCoordinator.ReconcileBatchAsync(batch.Id, cancellationToken);
        return (true, null);
    }
}

/// <summary>Records a channel rejection or a post-settlement return.</summary>
public record RejectPaymentBatchCommand(Guid PaymentBatchId, string Reason, bool IsReturn = false)
    : IRequest<(bool Success, string? Error)>;

public class RejectPaymentBatchHandler(IPayrollRepository repository)
    : IRequestHandler<RejectPaymentBatchCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        RejectPaymentBatchCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return (false, "سبب الرفض مطلوب.");

        var batch = await repository.PaymentBatches.FirstOrDefaultAsync(b => b.Id == request.PaymentBatchId, cancellationToken);
        if (batch is null) return (false, "دفعة الصرف غير موجودة.");

        var target = request.IsReturn ? PaymentBatchStatus.Returned : PaymentBatchStatus.Rejected;
        if (!PaymentBatchTransitions.CanTransition(batch.Status, target))
            return (false, $"لا يمكن الانتقال من {batch.Status} إلى {target}.");

        batch.Status = target;
        batch.RejectionReason = request.Reason.Trim();
        if (target == PaymentBatchStatus.Returned)
            batch.DisbursedAmount = 0;

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
