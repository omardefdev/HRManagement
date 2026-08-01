using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Services;

public static class PayrollRunCalculationSupport
{
    public static HashSet<DateOnly> BuildHolidaySet(
        DateOnly periodStart,
        DateOnly periodEnd,
        IEnumerable<OfficialHoliday> holidays)
    {
        var set = new HashSet<DateOnly>();
        foreach (var h in holidays)
        {
            if (h.IsRecurring)
            {
                for (var d = periodStart; d <= periodEnd; d = d.AddDays(1))
                {
                    if (d.Month == h.Date.Month && d.Day == h.Date.Day)
                        set.Add(d);
                }
            }
            else if (h.Date >= periodStart && h.Date <= periodEnd)
            {
                set.Add(h.Date);
            }
        }

        return set;
    }

    public static PayrollCalculationContext BuildContext(
        PayrollRun run,
        EmployeePayProfile profile,
        PayGrade? grade,
        PayrollConfiguration config,
        IReadOnlySet<DateOnly> extraHolidays,
        IEnumerable<AllowanceRecord> allowances,
        IEnumerable<DeductionRecord> deductions,
        IEnumerable<DeductionType> deductionTypes,
        IEnumerable<PayrollAdjustment> adjustments,
        IEnumerable<AbsenceDeduction> absenceDeductions,
        decimal? correctionDeltaGross = null,
        decimal? correctionDeltaNet = null)
    {
        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);
        var workingDays = PayrollPeriodRules.CountWorkingDays(periodStart, periodEnd, config, extraHolidays);
        var employeeDays = EmployeePayProfileRules.CountEmployeeWorkingDays(
            periodStart, periodEnd, profile.EffectiveFrom, profile.EffectiveTo, config, extraHolidays);

        return new PayrollCalculationContext
        {
            PayrollRunId = run.Id,
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            WorkingDaysInPeriod = workingDays,
            EmployeeWorkingDays = employeeDays,
            MonthlyBaseSalary = EmployeePayProfileRules.ResolveBaseSalary(profile, grade),
            Allowances = allowances.ToList(),
            Deductions = deductions.ToList(),
            StatutoryDeductionTypes = deductionTypes.ToList(),
            Adjustments = adjustments.ToList(),
            AbsenceDeductions = absenceDeductions.ToList(),
            Configuration = config,
            ExtraHolidays = extraHolidays,
            RunType = run.RunType,
            CorrectionDeltaGross = correctionDeltaGross,
            CorrectionDeltaNet = correctionDeltaNet
        };
    }
}
