using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace Payroll.Application.Features.PayrollRuns;

public record CreatePayrollRunCommand(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime RunDate,
    PayrollRunType RunType = PayrollRunType.Regular,
    Guid? SourceRunId = null) : IRequest<(bool Success, Guid? Id, string? Error)>;

public class CreatePayrollRunHandler(IPayrollRepository repository)
    : IRequestHandler<CreatePayrollRunCommand, (bool Success, Guid? Id, string? Error)>
{
    public async Task<(bool Success, Guid? Id, string? Error)> Handle(
        CreatePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        // PLAN-56 (review): a correction is made from its source (CreateCorrectionRunCommand): pinned to the source's framework and
        // period, one open correction per source, calculated from the adjustments aimed at it. This door would make one with none
        // of that.
        if (request.RunType == PayrollRunType.Correction)
            return (false, null, CorrectionRunMessages.CreatedFromItsSource);

        var periodErr = PayrollCalculation.ValidatePayrollPeriod(request.PeriodStart, request.PeriodEnd);
        if (periodErr != null) return (false, null, periodErr);

        var coverageErr = PayrollPeriodRules.ValidatePayrollPeriodCoverage(
            DateOnly.FromDateTime(request.PeriodStart),
            DateOnly.FromDateTime(request.PeriodEnd));
        if (coverageErr != null) return (false, null, coverageErr);

        var run = new PayrollRun
        {
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            RunDate = request.RunDate,
            Status = PayrollRunStatus.Draft,
            RunType = request.RunType,
            SourceRunId = request.SourceRunId,
            TotalNetPay = 0
        };

        await repository.AddAsync(run, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "يوجد تشغيل رواتب حي لنفس الفترة والنوع مسبقاً.");
        }

        return (true, run.Id, null);
    }
}

public record CalculatePayrollRunCommand(Guid PayrollRunId) : IRequest<(bool Success, int LinesGenerated, string? Error)>;

public class CalculatePayrollRunHandler(IMediator mediator, IPayrollRepository repository)
    : IRequestHandler<CalculatePayrollRunCommand, (bool Success, int LinesGenerated, string? Error)>
{
    public async Task<(bool Success, int LinesGenerated, string? Error)> Handle(
        CalculatePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        // PLAN-56 (G-PAY-03): a correction run that was reopened by a rejection is calculated again as a correction: from the
        // adjustments aimed at its source, through the validation.
        var runType = await repository.PayrollRuns.AsNoTracking()
            .Where(r => r.Id == request.PayrollRunId)
            .Select(r => (PayrollRunType?)r.RunType)
            .FirstOrDefaultAsync(cancellationToken);
        if (runType == PayrollRunType.Correction)
        {
            var corrected = await mediator.Send(new CalculateCorrectionRunCommand(request.PayrollRunId), cancellationToken);
            return (corrected.Success, corrected.LinesGenerated, corrected.Result.Error);
        }

        // PAY-003 / PERF-001: job-based path (snapshot → bounded workers → validate)
        var jobResult = await mediator.Send(new StartPayrollRunJobCommand(request.PayrollRunId), cancellationToken);
        return (jobResult.Success, jobResult.LinesGenerated, jobResult.Error);
    }
}


public record SubmitPayrollRunForReviewCommand(Guid PayrollRunId) : IRequest<(bool Success, string? Error)>;

public class SubmitPayrollRunForReviewHandler(IPayrollRepository repository)
    : IRequestHandler<SubmitPayrollRunForReviewCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        SubmitPayrollRunForReviewCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        try
        {
            run.SubmitForReview();
            await repository.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}

public record ApprovePayrollRunCommand(Guid PayrollRunId, Guid ApproverId, string? Role, string? Notes)
    : IRequest<RunActResult>;

/// <summary>PLAN-58 (D-PAY-5): the messages of the double approval.</summary>
public static class PayrollApprovalMessages
{
    public const string CreatorCannotApprove = "لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.";

    /// <summary>«اعتماد 1 من 2»: the run waits for its next approver.</summary>
    public static string Progress(int given, int required) =>
        FormattableString.Invariant($"اعتماد {given} من {required}");
}

/// <summary>
/// PLAN-58 (D-PAY-5, G-PAY-09): an approval of the run's present version. Above the agency's threshold
/// (<c>Payroll:Approval:DualApprovalThreshold</c>) the run needs two different approvers: the first leaves it «قيد المراجعة» («اعتماد 1
/// من 2»), the second, who is neither the first nor the creator, moves it to «معتمد». The run's id is the answer; its state and count are
/// read from the run.
/// </summary>
public class ApprovePayrollRunHandler(IPayrollRepository repository, SeparationOfDutiesService sod)
    : IRequestHandler<ApprovePayrollRunCommand, RunActResult>
{
    public async Task<RunActResult> Handle(
        ApprovePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null) return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);

        if (run.CreatedBy is Guid creatorId && creatorId == request.ApproverId)
            return RunActResult.Invalid(PayrollApprovalMessages.CreatorCannotApprove);

        var required = sod.GetRequiredApprovalCount(Options.PayrollSeparationOfDuties.RunEntityType, run.TotalNetPay);
        try
        {
            var countBefore = run.Approvals.Count;
            run.Approve(request.ApproverId, required, request.Role, request.Notes);
            foreach (var approval in run.Approvals.Skip(countBefore))
                repository.MarkAsAdded(approval);
            await repository.SaveChangesAsync(cancellationToken);
            return RunActResult.Done(run.Id);
        }
        catch (InvalidOperationException ex)
        {
            // A lost race on the run's row version is not caught here: it reaches the conflict middleware, which answers 409.
            return RunActResult.Invalid(ex.Message);
        }
    }
}

public record PostPayrollRunCommand(Guid PayrollRunId, Guid PosterId) : IRequest<(bool Success, string? Error)>;

/// <summary>PLAN-58 (D-PAY-3): the messages of the posting's separation of duties and review.</summary>
public static class PayrollPostingMessages
{
    public const string PosterIsApprover = "لا يمكن لنفس المستخدم اعتماد التشغيل ثم ترحيله.";
    public const string PosterIsReviewer = "لا يرحّل التشغيلَ من راجعه.";
    public const string ReviewRequired = "يجب أن يفتح مدقق سجل مراجعة للتشغيل ويغلقه مدقق آخر قبل الترحيل.";

    /// <summary>The run's log line when an agency turned the review off (<c>Payroll:Audit:Required=false</c>).</summary>
    public const string ReviewTurnedOff = "رُحّل التشغيل دون مراجعة مستقلة: الجهة عطّلت شرط المراجعة (Payroll:Audit:Required=false).";
}

public class PostPayrollRunHandler(
    IPayrollRepository repository,
    IPayrollEventPublisher eventPublisher,
    SeparationOfDutiesService sod,
    PaymentBatchIssuer issuer,
    Microsoft.Extensions.Options.IOptions<Options.PayrollAuditOptions>? auditOptions = null)
    : IRequestHandler<PostPayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        PostPayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        // PLAN-58 (G-PAY-09): the poster is none of the approvers — every approval of the run, not the latest.
        if (run.Approvals.Any(a => a.Decision == ApprovalDecision.Approved && a.ApproverId == request.PosterId))
            return (false, PayrollPostingMessages.PosterIsApprover);
        var (allowed, sodError) = PayrollSodGuard.ValidatePayrollRunAction(
            sod,
            SodAction.Post,
            request.PosterId,
            "payroll-poster",
            run.Id,
            run.CreatedBy,
            null,
            run.TotalNetPay);
        if (!allowed)
            return (false, sodError);

        // PLAN-57: the attendance gate's warning is not a review. PLAN-58 (D-PAY-3): the review is a record an auditor opened and another
        // auditor closed; neither of them posts. An agency may turn the review off by its own decision: the run's log then says so.
        var reviewRequired = auditOptions?.Value.Required ?? true;
        var reviews = await repository.PayrollAuditRecords.AsNoTracking()
            .Where(a => a.PayrollRunId == run.Id && a.Status == AuditStatus.Closed
                        && (a.Findings == null || !a.Findings.StartsWith(PayrollAttendanceGate.WarningPrefix)))
            .Select(a => new { a.AuditorId, a.ClosedBy })
            .ToListAsync(cancellationToken);
        if (reviews.Any(r => r.AuditorId == request.PosterId || r.ClosedBy == request.PosterId))
            return (false, PayrollPostingMessages.PosterIsReviewer);
        if (reviewRequired && reviews.Count == 0)
            return (false, PayrollPostingMessages.ReviewRequired);

        try
        {
            var postErr = run.ValidateCanPost();
            if (postErr != null) return (false, postErr);

            // PLAN-56 (review): a correction is posted beside a posted run. If its source was reversed since, there is nothing to
            // correct: the correction would pay or recover a difference on top of a run that paid nothing.
            if (run.RunType == PayrollRunType.Correction)
            {
                var sourcePosted = run.SourceRunId is Guid sourceId
                    && await repository.PayrollRuns.AsNoTracking()
                        .AnyAsync(r => r.Id == sourceId && r.Status == PayrollRunStatus.Posted, cancellationToken);
                if (!sourcePosted)
                    return (false, CorrectionRunMessages.SourceNoLongerPosted);
            }

            var job = await repository.PayrollRunJobs
                .Include(j => j.Partitions)
                .Where(j => j.PayrollRunId == run.Id)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (job is not null)
            {
                if (job.Partitions.Any(p => p.Status != PayrollRunPartitionStatus.Completed))
                    return (false, "لا يمكن ترحيل تشغيل بأقسام حساب غير مكتملة.");
                if (!job.ResultsValidated && !run.ValidatedAtCalculate)
                    return (false, "يجب نجاح التحقق قبل الترحيل.");
            }
            else if (!run.ValidatedAtCalculate)
            {
                return (false, "يجب نجاح التحقق قبل الترحيل.");
            }

            // PLAN-56 (G-PAY-03): the posting checks and writes nothing. The saved lines must still be sound, and still add up to the
            // total the validation froze: what is posted is what was approved. No line is assigned here.
            var saved = await PayrollRunLines.LoadAsync(repository, run, cancellationToken);
            var (totalsOk, totalsError) = PayrollRunReconciliation.ValidateTotals(
                run, saved.Lines, saved.ComponentsByLine, round: saved.Round);
            if (!totalsOk)
                return (false, $"{PayrollRunReconciliation.ResultsChangedSinceValidation} ({totalsError})");
            if (saved.SumNet != run.TotalNetPay)
                return (false, PayrollRunReconciliation.ResultsChangedSinceValidation);

            // PLAN-56 (G-PAY-06): the run keeps who posted it; the decider of its reversal is never that person.
            run.Post(request.PosterId, DateTime.UtcNow);

            // PLAN-58 (§1, P-5): the posting makes the batch and one order per line, through the issuer, in this save.
            await issuer.IssueAsync(run, saved.Lines, PaymentBatchSource.PayrollRunPosted, cancellationToken);

            if (job is not null)
            {
                job.Phase = PayrollRunJobPhase.Post;
                job.LastCheckpointAt = DateTime.UtcNow;
                if (!reviewRequired && reviews.Count == 0)
                    job.AppendLog(PayrollPostingMessages.ReviewTurnedOff);
            }

            // PLAN-40 (class B): one save writes the posted run, the batch, its orders and the outbox row together.
            await eventPublisher.PublishPayrollProcessedAsync(
                run.Id, run.PeriodStart, run.PeriodEnd, run.TotalNetPay, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}
