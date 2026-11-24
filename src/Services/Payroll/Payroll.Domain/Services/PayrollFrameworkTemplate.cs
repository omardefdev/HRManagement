using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-54 (§1): a draft of a framework from a legal regime's template: the regime's numeric fields, tax brackets and default
/// holidays as the platform knows them, in a v2 draft whose every value is still unclassified (interim, no reference, no exception).
/// Nothing in it governs pay until someone classifies the values (export, then import), and three people approve, attest and publish.
/// </summary>
public static class PayrollFrameworkTemplate
{
    public static bool IsKnownRegime(string? regimeCode) =>
        regimeCode is not null && LegalRegimeCodes.All.Contains(regimeCode);

    public static PayrollConfiguration CreateDraft(
        string regimeCode,
        string code,
        string? nameAr,
        int versionNumber,
        DateTime effectiveFrom)
    {
        var seed = regimeCode switch
        {
            LegalRegimeCodes.Law50_2004 => PayrollRegimeSeed.CreateLaw50Configuration(isActive: false),
            LegalRegimeCodes.Law53_2021 => PayrollRegimeSeed.CreateLaw53Configuration(isActive: false),
            LegalRegimeCodes.CivilService2026 => PayrollRegimeSeed.CreateCivilServiceConfiguration(isActive: false),
            _ => throw new ArgumentOutOfRangeException(nameof(regimeCode), regimeCode, "unknown legal regime"),
        };
        foreach (var holiday in PayrollRegimeSeed.CreateDefaultHolidays(seed.Id))
            seed.Holidays.Add(holiday);

        // The clone of a legacy version is exactly the unclassified v2 draft wanted; a template has no general tables to start from.
        var draft = PayrollConfigurationVersioning.CloneAsDraft(seed, versionNumber, effectiveFrom);
        draft.Code = code;
        draft.RegimeCode = regimeCode;
        draft.NameAr = string.IsNullOrWhiteSpace(nameAr) ? LegalRegimeCodes.DisplayNameAr(regimeCode) : nameAr.Trim();
        draft.SupersedesConfigurationId = null;
        draft.TenantId = Guid.Empty;
        foreach (var row in draft.TaxBrackets) row.TenantId = Guid.Empty;
        foreach (var row in draft.Holidays) row.TenantId = Guid.Empty;
        foreach (var row in draft.FieldSources) row.TenantId = Guid.Empty;
        return draft;
    }
}
