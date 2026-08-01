using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>GOV-002-A: seed LegalDecisionRecord rows from the platform register.</summary>
public static class LegalDecisionRegisterSeed
{
    public static IReadOnlyList<LegalDecisionRecord> CreateDefaults(Guid tenantId = default)
    {
        // Minimal embedded mirror of docs/legal-decisions/register.json for DB bootstrap.
        var rows = new (string id, string? field, string? rule, string source, string? issue, string owner, string status, string? regime, string? notes)[]
        {
            (LegalDecisionIds.TaxBrackets, "TaxBrackets", "TaxRule", "Law 50/2004 platform interim brackets", "LAW_50_2004", "Legal+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.TaxExemption, "TaxExemptionAmount", "TaxRule", "Platform interim default 0", "LAW_50_2004", "Legal+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.TaxableBase, "TaxableBaseMode", "TaxRule", "Platform interim GrossPay", "LAW_50_2004", "Legal+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.EosFormula, "EosGratuity*", null, "Law 50/2004 EOS interim rates", "LAW_50_2004", "Legal+HR+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.DailyDivisor, "DailyRateDivisor", "BaseSalaryRule", "Platform interim divisor 30", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.ProrationBasis, "ProrationDaysBasis", "BaseSalaryRule", "Platform interim WorkingDays", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.NonWorkingDays, "NonWorkingDays", null, "Platform interim Fri+Sat", "LAW_50_2004", "HR", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.OfficialHolidays, "OfficialHoliday", null, "Platform interim holiday seed", "SEED_HOLIDAYS", "HR", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.MissionAllowance, "MissionAllowanceAmount", null, "Platform interim default 0", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.SecondmentPercent, "SecondmentAllowancePercent", null, "Platform interim default 0", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.TrainingPremium, "TrainingPremium*", null, "Platform interim optional off", "LAW_50_2004", "HR+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.LeavePayoutDivisor, "LeavePayoutDailyDivisor", null, "Platform interim 30", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.RetroMonths, "MaxRetroCorrectionMonths", null, "Platform interim 12 months", "LAW_50_2004", "Legal+Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.Rounding, "RoundingMode,DecimalPlaces", "RoundingRule", "AwayFromZero scale 2", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.Currency, "CurrencyCode", null, "SYP platform interim", "LAW_50_2004", "Payroll", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.SingleRegime, "RegimeCode", null, "One regime per period", "GOV-002-C", "Legal", "ApprovedInterim", LegalRegimeCodes.Law50_2004, null),
            (LegalDecisionIds.CivilServicePending, "*", null, "Not enacted", "CIVIL_SERVICE_2026", "Legal", "PendingEnactment", LegalRegimeCodes.CivilService2026, "Do not treat as enacted"),
        };

        return rows.Select(r => new LegalDecisionRecord
        {
            Id = Guid.NewGuid(),
            DecisionId = r.id,
            ConfigField = r.field,
            RuleId = r.rule,
            OfficialSource = r.source,
            IssueRef = r.issue,
            EffectiveFrom = new DateTime(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Owner = r.owner,
            ApprovalStatus = r.status,
            RegimeCode = r.regime,
            Notes = r.notes,
            TenantId = tenantId
        }).ToList();
    }

    public static string RegisterJsonPathHint => "docs/legal-decisions/register.json";
}
