using Payroll.Domain.Entities;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Services;

/// <summary>PAY-003: one employee calculate package (persist atomically in one transaction).</summary>
public sealed class PayrollEmployeeCalculationPackage
{
    public required SalaryLine Line { get; init; }
    public required IReadOnlyList<SalaryLineComponent> Components { get; init; }
    public required IReadOnlyList<DeductionRecord> DeductionsToApply { get; init; }
    public required IReadOnlyList<PayrollAdjustment> AdjustmentsToApply { get; init; }
    public required IReadOnlyList<AbsenceDeduction> AbsencesToApply { get; init; }
}

public static class PayrollEmployeeCalculationUnit
{
    public static string ComputeProfileVersion(EmployeePayProfile profile) =>
        FormattableString.Invariant(
            $"{profile.UpdatedAt:O}|{profile.BaseSalary:F2}|{profile.PayGradeId:D}|{profile.EffectiveFrom:O}|{profile.EffectiveTo:O}");

    public static PayrollEmployeeCalculationPackage Build(
        IPayrollEngine engine,
        PayrollRun run,
        EmployeePayProfile profile,
        PayGrade? grade,
        PayrollConfiguration config,
        IReadOnlySet<DateOnly> extraHolidays,
        IEnumerable<AllowanceRecord> allowances,
        IList<DeductionRecord> deductions,
        IEnumerable<DeductionType> deductionTypes,
        IList<PayrollAdjustment> adjustments,
        IList<AbsenceDeduction> absences,
        decimal? correctionDeltaGross = null,
        decimal? correctionDeltaNet = null)
    {
        var context = PayrollRunCalculationSupport.BuildContext(
            run, profile, grade, config, extraHolidays,
            allowances, deductions, deductionTypes, adjustments, absences,
            correctionDeltaGross, correctionDeltaNet);

        var result = engine.Calculate(context);

        // Only what the engine actually charged is marked applied. Marking everything that was
        // loaded retired future advance instalments without ever deducting them.
        return new PayrollEmployeeCalculationPackage
        {
            Line = result.Line,
            Components = result.Components,
            DeductionsToApply = deductions.Where(d => result.ConsumedDeductionIds.Contains(d.Id)).ToList(),
            AdjustmentsToApply = adjustments.Where(a => result.ConsumedAdjustmentIds.Contains(a.Id)).ToList(),
            AbsencesToApply = absences.Where(a => result.ConsumedAbsenceIds.Contains(a.Id)).ToList()
        };
    }
}
