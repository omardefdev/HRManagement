using MediatR;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollRuns.Lifecycle;

/// <summary>
/// PLAN-56 (G-PAY-04): the reviewer rejects a run with a note, and the run reopens: review → reject → correct → resubmit. The
/// rejected version is kept as a <see cref="PayrollRunRevision"/>, its lines are deleted, its inputs are released, and the run is a
/// draft again with its version one more — all in one transaction. A rejection without a note is refused.
/// </summary>
public record RejectPayrollRunCommand(Guid PayrollRunId, Guid ApproverId, string? Notes) : IRequest<RunActResult>;

public class RejectPayrollRunHandler(IPayrollRepository repository, IPayrollCalendar? calendar = null)
    : IRequestHandler<RejectPayrollRunCommand, RunActResult>
{
    public const string ReleaseReason = "رفض التشغيل وإعادة فتحه";

    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<RunActResult> Handle(RejectPayrollRunCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
            return RunActResult.Invalid(PayrollRun.RejectionNoteRequired);

        repository.ClearChangeTracker();
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (PayrollRunTransitions.Refusal(PayrollRunAct.Reject, run.Status) is { } notNow)
            return RunActResult.Invalid(notNow);

        // What the rejected version was, read before anything changes.
        var now = _calendar.UtcNow;
        var snapshot = await PayrollRunLifecycle.SnapshotLinesAsync(repository, run.Id, cancellationToken);
        var rejected = new PayrollRunRevision
        {
            TenantId = run.TenantId,
            PayrollRunId = run.Id,
            Revision = run.Revision,
            LineCount = snapshot.LineCount,
            TotalNetPay = run.TotalNetPay,
            LinesFingerprint = snapshot.Fingerprint,
            RejectedBy = request.ApproverId,
            RejectedAt = now,
            Note = request.Notes.Trim().Length <= PayrollRunRevision.NoteMaxLength
                ? request.Notes.Trim()
                : request.Notes.Trim()[..PayrollRunRevision.NoteMaxLength],
        };

        // The run's own row first: a decision made at the same moment by another reviewer (an approval, another rejection) wins or
        // loses here, on the row's version, before a line is deleted or an input released. The loser changes nothing.
        var decision = run.ReopenAfterRejection(request.ApproverId, now, request.Notes);
        repository.MarkAsAdded(decision);
        await repository.AddAsync(rejected, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // The inputs before the lines: a recovery deduction names its line.
        await PayrollInputRelease.ReleaseAsync(repository, run, ReleaseReason, request.ApproverId, cancellationToken);
        await PayrollRunLifecycle.DeleteLinesAsync(repository, run, ReleaseReason, request.ApproverId, snapshot, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return RunActResult.Done(run.Id);
    }
}
