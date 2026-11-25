using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.BankAccounts;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Features.PayrollRuns;

/// <summary>
/// PLAN-56 (G-PAY-03): a correction run of a posted run. It takes only the approved adjustments aimed at its source
/// (<see cref="PayrollAdjustment.TargetSourceRunId"/>); it has no base and no tax on the difference (D-PAY-17); it passes the same
/// validation as a regular run before it is marked validated; and a line whose parts fall below zero pays nothing and leaves a
/// recovery deduction for the next cycle instead of the difference becoming zero. One correction not yet posted per source run.
/// </summary>
public record CreateCorrectionRunCommand(
    Guid SourceRunId,
    DateTime RunDate) : IRequest<CorrectionRunResult>;

/// <summary>
/// PLAN-56: calculates a correction run again after a rejection reopened it (a draft with no lines): from the adjustments aimed at
/// its source that are free now, through the validation, like its first calculation.
/// </summary>
public record CalculateCorrectionRunCommand(Guid CorrectionRunId) : IRequest<CorrectionRunResult>;

/// <summary>
/// The answer of the correction commands. It deconstructs like the older <c>(Success, CorrectionRunId, Error)</c>, and carries the
/// kind of a refusal (<see cref="RunActResult"/>): 400 a rule, 404 no such run, 409 a second open correction of the same source.
/// </summary>
public sealed record CorrectionRunResult(RunActResult Result, int LinesGenerated = 0)
{
    public bool Success => Result.Success;

    public Guid? CorrectionRunId => Result.Id;

    public string? Error => Result.Error;

    public void Deconstruct(out bool success, out Guid? correctionRunId, out string? error)
    {
        success = Result.Success;
        correctionRunId = Result.Id;
        error = Result.Error;
    }

    public static CorrectionRunResult Refused(RunActResult refusal) => new(refusal);
}

public static class CorrectionRunMessages
{
    public const string SourceNotFound = "التشغيل المرجعي غير موجود.";
    public const string SourceNotPosted = "التصحيح الرجعي يتطلب تشغيلاً مرحّلاً.";
    public const string SourceNotPinned = "التشغيل المرجعي بلا نسخة إعدادات مثبتة؛ أعد حساب/ترحيل المصدر وفق PAY-002.";
    public const string FrameworkMissing = "نسخة الإعدادات المثبتة على التشغيل المرجعي غير موجودة.";
    public const string FrameworkChanged = "بصمة إعدادات التشغيل المرجعي لا تطابق النسخة القانونية.";

    /// <summary>A correction is made of adjustments aimed at its source; with none there is nothing to correct.</summary>
    public const string NoTargetedAdjustments =
        "لا توجد تسويات معتمدة موجّهة إلى هذا التشغيل. أدخل تسوية موجّهة إليه (من صفحة تسويات الراتب) واعتمدها، ثم أنشئ التصحيح.";

    public const string OpenCorrectionExists =
        "لهذا التشغيل تصحيح لم يُرحَّل بعد. رحِّله أو ألغِه قبل إنشاء تصحيح آخر للتشغيل نفسه.";

    public const string NotACorrection = "هذا التشغيل ليس تشغيل تصحيح.";

    public const string NotCalculatedByAJob =
        "تشغيل التصحيح لا يُحسب بمهمة حساب على ملفات الأجر؛ بنوده هي التسويات الموجّهة إلى تشغيله المرجعي.";

    public const string ReleaseReason = "إعادة حساب تشغيل التصحيح";
}

public class CreateCorrectionRunHandler(
    IPayrollRepository repository,
    IPayrollEngine engine,
    IPayrollLegalControlGuard? legalControlGuard = null,
    IPayrollCalendar? calendar = null)
    : IRequestHandler<CreateCorrectionRunCommand, CorrectionRunResult>,
      IRequestHandler<CalculateCorrectionRunCommand, CorrectionRunResult>
{
    private readonly IPayrollCalendar _calendar = calendar ?? new PayrollCalendar(TimeProvider.System);

    public async Task<CorrectionRunResult> Handle(CreateCorrectionRunCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var (source, config, refusal) = await SourceAsync(request.SourceRunId, request.RunDate, cancellationToken);
        if (refusal is not null)
            return CorrectionRunResult.Refused(refusal);

        // One correction not yet posted per source run. Checked here for the message; the unique index decides a race.
        if (await repository.PayrollRuns.AnyAsync(
                r => r.RunType == PayrollRunType.Correction && r.SourceRunId == source!.Id
                     && r.Status != PayrollRunStatus.Posted && r.Status != PayrollRunStatus.Reversed && r.Status != PayrollRunStatus.Cancelled,
                cancellationToken))
            return CorrectionRunResult.Refused(RunActResult.Conflict(CorrectionRunMessages.OpenCorrectionExists));

        var correction = new PayrollRun
        {
            TenantId = source!.TenantId,
            PeriodStart = source.PeriodStart,
            PeriodEnd = source.PeriodEnd,
            RunDate = request.RunDate,
            Status = PayrollRunStatus.Draft,
            RunType = PayrollRunType.Correction,
            SourceRunId = source.Id,
            PayrollConfigurationId = source.PayrollConfigurationId,
            ConfigContentHash = source.ConfigContentHash
        };

        // The run, its lines, its recoveries and its validation are one unit: a correction that does not validate is not created.
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        try
        {
            await repository.AddAsync(correction, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ConcurrencyExtensions.TryGetUniqueConstraintName(ex, out var constraint)
                ? constraint == PayrollRunIndexNames.OneOpenCorrectionPerSource
                : ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            return CorrectionRunResult.Refused(RunActResult.Conflict(CorrectionRunMessages.OpenCorrectionExists));
        }

        var calculated = await CalculateAsync(correction, source, config!, cancellationToken);
        if (!calculated.Success)
            return calculated; // the transaction is rolled back with its scope: nothing of the correction is left

        await transaction.CommitAsync(cancellationToken);
        return calculated;
    }

    public async Task<CorrectionRunResult> Handle(CalculateCorrectionRunCommand request, CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        var correction = await repository.GetPayrollRunAsync(request.CorrectionRunId, includeLines: false, cancellationToken);
        if (correction is null)
            return CorrectionRunResult.Refused(RunActResult.NotFound(RunLifecycleMessages.RunNotFound));
        if (correction.RunType != PayrollRunType.Correction || correction.SourceRunId is not Guid sourceId)
            return CorrectionRunResult.Refused(RunActResult.Invalid(CorrectionRunMessages.NotACorrection));
        if (correction.ValidateCanCalculate() is { } cannotCalculate)
            return CorrectionRunResult.Refused(RunActResult.Invalid(cannotCalculate));

        var (source, config, refusal) = await SourceAsync(sourceId, correction.RunDate, cancellationToken);
        if (refusal is not null)
            return CorrectionRunResult.Refused(refusal);

        // A calculation again: whatever an earlier attempt left is given back first, then the lines are built anew.
        await PayrollInputRelease.ReleaseAsync(repository, correction, CorrectionRunMessages.ReleaseReason, actorId: null, cancellationToken);
        await PayrollRunLifecycle.DeleteLinesAsync(
            repository, correction, CorrectionRunMessages.ReleaseReason, actorId: null, snapshot: null, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var calculated = await CalculateAsync(correction, source!, config!, cancellationToken);
        if (!calculated.Success)
            return calculated;

        await transaction.CommitAsync(cancellationToken);
        return calculated;
    }

    /// <summary>The source run and its pinned framework version, or why a correction of it cannot be calculated now.</summary>
    private async Task<(PayrollRun? Source, PayrollConfiguration? Config, RunActResult? Refusal)> SourceAsync(
        Guid sourceRunId, DateTime runDate, CancellationToken cancellationToken)
    {
        var source = await repository.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == sourceRunId, cancellationToken);
        if (source is null)
            return (null, null, RunActResult.NotFound(CorrectionRunMessages.SourceNotFound));
        if (source.Status != PayrollRunStatus.Posted)
            return (null, null, RunActResult.Invalid(CorrectionRunMessages.SourceNotPosted));
        if (!source.PayrollConfigurationId.HasValue || string.IsNullOrEmpty(source.ConfigContentHash))
            return (null, null, RunActResult.Invalid(CorrectionRunMessages.SourceNotPinned));

        var config = await repository.GetConfigurationByIdAsync(source.PayrollConfigurationId.Value, cancellationToken);
        if (config is null)
            return (null, null, RunActResult.Invalid(CorrectionRunMessages.FrameworkMissing));
        if (!string.Equals(source.ConfigContentHash, config.ContentHash, StringComparison.OrdinalIgnoreCase))
            return (null, null, RunActResult.Invalid(CorrectionRunMessages.FrameworkChanged));

        // PLAN-54 (§3): a correction is a calculation with the source's pinned version. If that version holds interim values, every
        // exception it cites must be active today (the payroll calendar's date), as for the run itself. Nothing is written on refusal.
        if (Features.PayrollConfigurations.PayrollInterimGate.RefusalForRun(config, legalControlGuard, _calendar) is { } interimRefusal)
            return (null, null, RunActResult.Invalid(interimRefusal));

        if (config.MaxRetroCorrectionMonths > 0)
        {
            var months = ((runDate.Year - source.PeriodStart.Year) * 12) + runDate.Month - source.PeriodStart.Month;
            if (months > config.MaxRetroCorrectionMonths)
                return (null, null, RunActResult.Invalid($"تجاوز حد التصحيح الرجعي ({config.MaxRetroCorrectionMonths} شهر)."));
        }

        return (source, config, null);
    }

    /// <summary>
    /// The correction's lines, in the caller's transaction: one line per employee of the source run who has approved adjustments
    /// aimed at it; a recovery deduction for each line whose parts fall below zero; then the validation a regular run passes, and
    /// only then the run is marked validated. Nothing freezes it by hand.
    /// </summary>
    private async Task<CorrectionRunResult> CalculateAsync(
        PayrollRun correction, PayrollRun source, PayrollConfiguration config, CancellationToken cancellationToken)
    {
        // Only the adjustments aimed at this source: approved by a second person (PLAN-54, D-PAY-6) and not yet applied in any run.
        var targeted = await repository.PayrollAdjustments
            .Where(a => a.TargetSourceRunId == source.Id && a.PayrollRunId == null
                        && a.ApprovalStatus == ManualInputApprovalStatus.Approved)
            .OrderBy(a => a.EmployeeId).ThenBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        if (targeted.Count == 0)
            return CorrectionRunResult.Refused(RunActResult.Invalid(CorrectionRunMessages.NoTargetedAdjustments));

        // A correction corrects lines of its source: an adjustment for an employee the source did not pay has no line to correct.
        var employees = targeted.Select(a => a.EmployeeId).Distinct().ToList();
        var inSource = (await repository.SalaryLines.AsNoTracking()
                .Where(l => l.PayrollRunId == source.Id && employees.Contains(l.EmployeeId))
                .Select(l => l.EmployeeId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var periodStart = DateOnly.FromDateTime(correction.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(correction.PeriodEnd);
        // PLAN-54: a v2 version brings its own holidays and statutory deductions; only a legacy version reads the general tables.
        var reference = await PayrollFrameworkReferenceData.LoadAsync(repository, config, cancellationToken);
        var extraHolidays = PayrollRunCalculationSupport.BuildHolidaySet(periodStart, periodEnd, reference.Holidays);

        // PLAN-55 (D-PAY-15): a correction run writes no snapshot rows; its lines are computed here, so the account in force now is the
        // one each line pins.
        var pinnedAccounts = await BankAccountPinning.ActiveAccountsAsync(repository, cancellationToken);

        correction.BeginCalculating();
        var lines = new List<SalaryLine>();
        var recoveryDue = CorrectionRecovery.DueDateAfter(source.PeriodEnd);
        foreach (var group in targeted.Where(a => inSource.Contains(a.EmployeeId)).GroupBy(a => a.EmployeeId))
        {
            var profile = await repository.GetActiveProfileAsync(group.Key, cancellationToken);
            if (profile is null) continue;

            var package = PayrollEmployeeCalculationUnit.Build(
                engine, correction, profile, profile.PayGrade, config, extraHolidays,
                Array.Empty<AllowanceRecord>(),
                Array.Empty<DeductionRecord>(),
                reference.LegacyDeductionTypes,
                group.ToList(),
                Array.Empty<AbsenceDeduction>(),
                bankAccountId: pinnedAccounts.TryGetValue(group.Key, out var pinned) ? pinned : null);

            var (saved, _) = await repository.SaveEmployeePackageAtomicAsync(correction.Id, package, cancellationToken);
            if (!saved) continue;
            lines.Add(package.Line);

            // A negative difference: the line pays nothing, and the shortfall is taken back in the next cycle (the first day of the
            // period after the corrected one), by a deduction the system makes and no person has to approve.
            if (PayrollNet.Shortfall(package.Line) is var shortfall and > 0m)
            {
                await repository.AddAsync(
                    new DeductionRecord
                    {
                        TenantId = correction.TenantId,
                        EmployeeId = group.Key,
                        DeductionType = CorrectionRecovery.DeductionType,
                        Amount = shortfall,
                        Reason = $"استرداد فرق تصحيح فترة {source.PeriodStart:yyyy-MM}",
                        DueDate = recoveryDue,
                        SourceReference = CorrectionRecovery.SourceReferenceOf(package.Line.Id),
                        // Its origin and its approval state are the entity's defaults: of the system, approved (PLAN-54's guard
                        // lets only the manual input commands say otherwise).
                    },
                    cancellationToken);
            }
        }

        if (lines.Count == 0)
            return CorrectionRunResult.Refused(RunActResult.Invalid(CorrectionRunMessages.NoTargetedAdjustments));
        await repository.SaveChangesAsync(cancellationToken);

        // The validation a regular run passes, on what was saved.
        var saved2 = await PayrollRunLines.LoadAsync(repository, correction, cancellationToken);
        var (ok, error) = PayrollRunReconciliation.ValidateLineIntegrity(
            saved2.Lines, saved2.ComponentsByLine, expectedEmployeeCount: lines.Count, round: config.RoundMoney);
        if (!ok)
            return CorrectionRunResult.Refused(RunActResult.Invalid(error ?? "فشل التحقق من تشغيل التصحيح."));

        correction.MarkValidated(saved2.SumNet);
        await repository.SaveChangesAsync(cancellationToken);
        return new CorrectionRunResult(RunActResult.Done(correction.Id), lines.Count);
    }
}
