using System.Text.Json;
using HRManagement.SharedKernel.Entities;
using Payroll.Application.Abstractions;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-56 (G-PAY-07): frees what a run's calculation consumed, so that no input stays attached to a cancelled run, a rejected
/// version, a restarted calculation or a reversed run. An input applied in a run was lost for good when that run was cancelled: the
/// loader reads only an input that is free or applied in the same run.
/// <para>
/// It runs inside the caller's transaction and is idempotent. It touches the applied markers only
/// (<c>DeductionRecord.AppliedInRunId</c>, <c>AbsenceDeduction.AppliedInRunId</c>, <c>PayrollAdjustment.PayrollRunId</c>) and no
/// other field, so PLAN-57 adds its own fields to an absence (<c>CarryForward</c>, <c>OriginalPeriodStart/End</c>,
/// <c>SourceVersion</c>) without changing it: a reopened run then picks up the attendance differences of its own period. It also
/// deletes the recovery deductions a correction run created that no run has applied. A payment hold is never touched: holds are
/// controls, released only by their four-eyes release.
/// </para>
/// Its callers: the cancellation, the rejection, the restart of a calculation and the approved reversal.
/// </summary>
public static class PayrollInputRelease
{
    /// <summary>The action name of the audit row a release writes on its run.</summary>
    public const string AuditAction = "InputsReleased";

    public static async Task<PayrollRunInputsReleased> ReleaseAsync(
        IPayrollRepository repository,
        Payroll.Domain.Entities.PayrollRun run,
        string reason,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var payrollRunId = run.Id;
        var released = await repository.ReleaseRunInputsAsync(payrollRunId, cancellationToken);

        // One row on the run says what was freed and why. A release that found nothing (a second call) writes none.
        if (released.Total > 0)
        {
            await repository.AddAsync(
                new EntityAuditLog
                {
                    TenantId = run.TenantId,
                    EntityType = nameof(Payroll.Domain.Entities.PayrollRun),
                    EntityId = payrollRunId,
                    Action = AuditAction,
                    UserId = actorId,
                    ChangedFields = JsonSerializer.Serialize(new
                    {
                        reason,
                        deductions = released.Deductions,
                        absences = released.Absences,
                        adjustments = released.Adjustments,
                        recoveriesDeleted = released.RecoveriesDeleted,
                    }),
                },
                cancellationToken);
        }

        return released;
    }
}

/// <summary>
/// PLAN-58 (P-11, PLAN-57's handover): two regular runs of one agency calculating at once both saw a free deduction, adjustment or
/// absence row; the second to save finds it taken by the first. The save is refused (a concurrency failure, as the row version also
/// refuses a race between the read and the save), the partition fails, and the run's calculation is restarted with a new snapshot (PLAN-56),
/// as an input-fingerprint conflict is. An input is never marked by two runs.
/// </summary>
public sealed class PayrollInputTakenException : Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException
{
    public const string TakenMessage =
        "مدخل راتب (خصم أو تسوية أو غياب) أخذه تشغيل آخر يُحسب في الوقت نفسه؛ أُوقف الحساب ولم يُكتب شيء من هذه الدفعة. أعد بدء الحساب ليأخذ لقطة جديدة.";

    public PayrollInputTakenException() : base(TakenMessage)
    {
    }

    /// <summary>Throws when the input is marked by another run than <paramref name="payrollRunId"/>.</summary>
    public static void ThrowIfTaken(Guid? markedBy, Guid payrollRunId)
    {
        if (markedBy is Guid other && other != payrollRunId)
            throw new PayrollInputTakenException();
    }
}
