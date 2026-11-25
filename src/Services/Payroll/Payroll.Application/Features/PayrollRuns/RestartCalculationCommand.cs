using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollRuns;

/// <summary>
/// PLAN-56 (G-PAY-07): the way out of a calculation that cannot be resumed. A job that failed (a pay profile or an input changed
/// since its snapshot: the fingerprint conflict) or was paused is given up: the job is cancelled, the lines it wrote are deleted,
/// the inputs it attached are released, the run is a draft again, and then a calculation starts with a new snapshot — through
/// <see cref="StartPayrollRunJobCommand"/>, so through every gate a first calculation passes. Resuming such a job reruns the same
/// snapshot and fails the same way.
/// </summary>
public record RestartCalculationCommand(Guid JobId, Guid ActorId)
    : IRequest<(RunActResult Result, int LinesGenerated, Guid? NewJobId)>;

public class RestartCalculationHandler(IPayrollRepository repository, IMediator mediator)
    : IRequestHandler<RestartCalculationCommand, (RunActResult Result, int LinesGenerated, Guid? NewJobId)>
{
    public const string OnlyFailedOrPaused = "لا يُعاد بدء الحساب إلا لمهمة فاشلة أو متوقفة مؤقتاً.";
    public const string ReleaseReason = "إعادة بدء الحساب بلقطة جديدة";

    public async Task<(RunActResult Result, int LinesGenerated, Guid? NewJobId)> Handle(
        RestartCalculationCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        Guid runId;
        await using (var transaction = await repository.BeginTransactionAsync(cancellationToken))
        {
            var job = await repository.PayrollRunJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
            if (job is null)
                return (RunActResult.NotFound(RunLifecycleMessages.JobNotFound), 0, null);
            if (job.Status is not (PayrollRunJobStatus.Failed or PayrollRunJobStatus.Paused))
                return (RunActResult.Invalid(OnlyFailedOrPaused), 0, null);

            var run = await repository.GetPayrollRunAsync(job.PayrollRunId, includeLines: false, cancellationToken);
            if (run is null)
                return (RunActResult.NotFound(RunLifecycleMessages.RunNotFound), 0, null);
            if (run.ResultsFrozen)
                return (RunActResult.Invalid(PayrollRun.RestartNeedsUnfrozenResults), 0, null);
            if (PayrollRunTransitions.Refusal(PayrollRunAct.RestartCalculation, run.Status) is { } notNow)
                return (RunActResult.Invalid(notNow), 0, null);

            await repository.StopRunJobsAsync(run.Id, cancellationToken);
            run.ResetForRestart();
            await repository.SaveChangesAsync(cancellationToken);

            // The inputs before the lines: a recovery deduction names its line.
            await PayrollInputRelease.ReleaseAsync(repository, run, ReleaseReason, request.ActorId, cancellationToken);
            await PayrollRunLifecycle.DeleteLinesAsync(repository, run, ReleaseReason, request.ActorId, snapshot: null, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            runId = run.Id;
        }

        // A calculation like any other: a new job, a new snapshot of the profiles, the accounts and the inputs, and the same gates.
        repository.ClearChangeTracker();
        var started = await mediator.Send(new StartPayrollRunJobCommand(runId), cancellationToken);
        return started.Success
            ? (RunActResult.Done(runId), started.LinesGenerated, started.JobId)
            : (RunActResult.Invalid(started.Error ?? "تعذّر بدء الحساب من جديد."), started.LinesGenerated, started.JobId);
    }
}
