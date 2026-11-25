using MediatR;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollRuns;

/// <summary>
/// PLAN-56 (G-PAY-04, D-PAY-18): any run that is not posted can be cancelled, with a reason. Its period is then free for a new run,
/// and everything its calculation consumed is free again. The processor cancels what was not sent for review; a run under review or
/// approved is cancelled by the holder of the approval policy. <paramref name="ActorMayProcess"/> and
/// <paramref name="ActorMayApprove"/> are what the caller's policies say of the actor; the handler applies them to the state it
/// reads, in the same transaction as the cancellation.
/// </summary>
public record CancelPayrollRunCommand(Guid PayrollRunId, Guid ActorId, string? Reason, bool ActorMayProcess, bool ActorMayApprove)
    : IRequest<RunActResult>;

public class CancelPayrollRunHandler(IPayrollRepository repository, IPayrollCalendar? calendar = null)
    : IRequestHandler<CancelPayrollRunCommand, RunActResult>
{
    public const string ReleaseReason = "إلغاء التشغيل";

    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    /// <summary>D-PAY-18: the states in which cancelling is the approver's decision.</summary>
    public static bool NeedsApprover(PayrollRunStatus status) =>
        status is PayrollRunStatus.UnderReview or PayrollRunStatus.Approved;

    public async Task<RunActResult> Handle(CancelPayrollRunCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return RunActResult.Invalid(PayrollRun.CancellationReasonRequired);

        repository.ClearChangeTracker();
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null)
            return RunActResult.NotFound(RunLifecycleMessages.RunNotFound);
        if (PayrollRunTransitions.Refusal(PayrollRunAct.Cancel, run.Status) is { } notNow)
            return RunActResult.Invalid(notNow);

        if (NeedsApprover(run.Status) ? !request.ActorMayApprove : !request.ActorMayProcess)
        {
            return RunActResult.Forbidden(NeedsApprover(run.Status)
                ? RunLifecycleMessages.CancelAfterReviewNeedsApprover
                : RunLifecycleMessages.CancelBeforeReviewNeedsProcessor);
        }

        if (await PayrollRunLifecycle.HasFundingOrPartialDisbursementAsync(repository, run, cancellationToken))
            return RunActResult.Invalid(RunLifecycleMessages.HasFundingOrPartialDisbursement);

        // The jobs first: this waits for a worker's save in flight and holds the jobs against any later one until the commit, so the
        // release below sees every input the workers attached, and no worker writes a line or an input after it.
        await repository.StopRunJobsAsync(run.Id, cancellationToken);

        var decision = run.Cancel(request.ActorId, _calendar.UtcNow, request.Reason);
        repository.MarkAsAdded(decision);
        await repository.SaveChangesAsync(cancellationToken);

        await PayrollInputRelease.ReleaseAsync(repository, run, ReleaseReason, request.ActorId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return RunActResult.Done(run.Id);
    }
}
