using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayrollRuns.Review;

/// <summary>PLAN-58 (D-PAY-3): the Arabic messages of the independent review.</summary>
public static class PayrollAuditReviewMessages
{
    public const string RecordNotFound = "سجل المراجعة غير موجود.";
    public const string RunNotReviewable = "يُفتح سجل المراجعة لتشغيل قيد المراجعة أو معتمد فقط.";
    public const string AlreadyClosed = "سجل المراجعة مغلق بالفعل.";
    public const string WarningIsNotAReview = "تحذير بوابة الحضور ليس سجل مراجعة ولا يُغلق يدوياً: يزول عند إعادة الحساب بعد إغلاق حضور الفترة.";
    public const string CloserIsOpener = "لا يُغلق سجل المراجعة من فتحه: يغلقه مدقق آخر.";
    public const string ReviewerMadeTheRun = "لا يراجع التشغيلَ منشئُه.";
    public const string ReviewerApprovedTheRun = "لا يراجع التشغيلَ أحدُ معتمديه.";
    public const string ReviewerPostedTheRun = "لا يراجع التشغيلَ مُرحِّله.";
}

/// <summary>
/// PLAN-58 (D-PAY-3): a reviewer is none of the run's makers: not its creator, not any of its approvers (every version), not its poster.
/// </summary>
internal static class PayrollReviewerRules
{
    public static string? Refusal(PayrollRun run, Guid reviewer)
    {
        if (run.CreatedBy is Guid creator && creator == reviewer)
            return PayrollAuditReviewMessages.ReviewerMadeTheRun;
        if (run.Approvals.Any(a => a.Decision == ApprovalDecision.Approved && a.ApproverId == reviewer))
            return PayrollAuditReviewMessages.ReviewerApprovedTheRun;
        if (run.KnownPoster is Guid poster && poster == reviewer)
            return PayrollAuditReviewMessages.ReviewerPostedTheRun;
        return null;
    }
}

/// <summary>
/// PLAN-58 (review): the review records of the run's present version — opened after its last rejection (a rejection reopens the run, its
/// lines are calculated again, and a review of the earlier figures no longer counts). The attendance gate's warning is not a review.
/// </summary>
public static class PayrollReviewRecords
{
    public sealed record Reviewer(Guid? OpenedBy, Guid? ClosedBy, AuditStatus Status);

    public static async Task<IReadOnlyList<Reviewer>> OfThisVersionAsync(IPayrollRepository repository, Guid runId, CancellationToken ct)
    {
        var reopenedAt = await repository.PayrollRunRevisions.AsNoTracking()
            .Where(r => r.PayrollRunId == runId)
            .Select(r => (DateTime?)r.RejectedAt)
            .MaxAsync(ct);
        var records = repository.PayrollAuditRecords.AsNoTracking()
            .Where(a => a.PayrollRunId == runId && (a.Findings == null || !a.Findings.StartsWith(PayrollAttendanceGate.WarningPrefix)));
        if (reopenedAt is DateTime since)
            records = records.Where(a => a.CreatedAt > since);
        return await records.Select(a => new Reviewer(a.AuditorId, a.ClosedBy, a.Status)).ToListAsync(ct);
    }

    /// <summary>Whether <paramref name="user"/> opened or closed a review of the present version.</summary>
    public static bool Reviewed(IEnumerable<Reviewer> reviews, Guid user) =>
        reviews.Any(r => r.OpenedBy == user || r.ClosedBy == user);
}

/// <summary>An auditor opens the review record of a run under review or approved, with his findings so far.</summary>
public sealed record OpenPayrollAuditRecordCommand(Guid PayrollRunId, string? Findings, Guid AuditorId) : IRequest<RunActResult>;

public sealed class OpenPayrollAuditRecordHandler(IPayrollRepository repository) : IRequestHandler<OpenPayrollAuditRecordCommand, RunActResult>
{
    public async Task<RunActResult> Handle(OpenPayrollAuditRecordCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (run.Status is not (PayrollRunStatus.UnderReview or PayrollRunStatus.Approved))
            return RunActResult.Invalid(PayrollAuditReviewMessages.RunNotReviewable);
        if (PayrollReviewerRules.Refusal(run, request.AuditorId) is { } refusal)
            return RunActResult.Invalid(refusal);

        var record = new PayrollAuditRecord
        {
            TenantId = run.TenantId,
            PayrollRunId = run.Id,
            AuditorId = request.AuditorId,
            Findings = string.IsNullOrWhiteSpace(request.Findings) ? null : request.Findings.Trim(),
            Status = AuditStatus.Open,
        };
        await repository.AddAsync(record, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return RunActResult.Done(record.Id);
    }
}

/// <summary>
/// Another auditor closes the record: not its opener, and none of the run's makers. The attendance gate's warning is not a review and is
/// never closed.
/// </summary>
public sealed record ClosePayrollAuditRecordCommand(Guid RecordId, Guid ClosedBy) : IRequest<RunActResult>;

public sealed class ClosePayrollAuditRecordHandler(IPayrollRepository repository) : IRequestHandler<ClosePayrollAuditRecordCommand, RunActResult>
{
    public async Task<RunActResult> Handle(ClosePayrollAuditRecordCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var record = await repository.PayrollAuditRecords.FirstOrDefaultAsync(a => a.Id == request.RecordId, cancellationToken);
        if (record is null)
            return RunActResult.NotFound(PayrollAuditReviewMessages.RecordNotFound);
        if (PayrollAttendanceGate.IsWarning(record))
            return RunActResult.Invalid(PayrollAuditReviewMessages.WarningIsNotAReview);
        if (record.Status != AuditStatus.Open)
            return RunActResult.Conflict(PayrollAuditReviewMessages.AlreadyClosed);
        if (record.AuditorId is Guid opener && opener == request.ClosedBy)
            return RunActResult.Invalid(PayrollAuditReviewMessages.CloserIsOpener);
        var run = await repository.GetPayrollRunAsync(record.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (PayrollReviewerRules.Refusal(run, request.ClosedBy) is { } refusal)
            return RunActResult.Invalid(refusal);

        record.Status = AuditStatus.Closed;
        record.ClosedAt = DateTime.UtcNow;
        record.ClosedBy = request.ClosedBy;
        await repository.SaveChangesAsync(cancellationToken);
        return RunActResult.Done(record.Id);
    }
}
