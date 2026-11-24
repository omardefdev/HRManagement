using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-54 (§2, P-7): what a calculation reads beside the pinned version itself. A run of a v2 version reads nothing: its statutory
/// deductions and its holidays are the version's own rows, inside its fingerprint, and no general table that anyone can edit decides
/// it. A run of a version older than PLAN-54 reads, exactly as before, the general deduction types and the general holidays with the
/// version's own: the legacy path, named so in the job's log.
/// </summary>
public sealed record PayrollFrameworkReferenceData(
    IReadOnlyList<OfficialHoliday> Holidays,
    IReadOnlyList<DeductionType> LegacyDeductionTypes,
    bool IsLegacy)
{
    public const string LegacyLogLine =
        "مسار قديم: نسخة الإطار المثبتة أقدم من حزم الإطار القانوني (بصمة v1)؛ الخصومات النظامية والعطل العامة تُقرأ من الجداول العامة كما كانت.";

    public static async Task<PayrollFrameworkReferenceData> LoadAsync(
        IPayrollRepository repository,
        PayrollConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.IsLegacyFramework)
            return new PayrollFrameworkReferenceData(config.Holidays.ToList(), [], IsLegacy: false);

        var holidays = await repository.OfficialHolidays
            .Where(h => h.PayrollConfigurationId == null || h.PayrollConfigurationId == config.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var deductionTypes = await repository.DeductionTypes.AsNoTracking().ToListAsync(cancellationToken);
        return new PayrollFrameworkReferenceData(holidays, deductionTypes, IsLegacy: true);
    }
}
