namespace Payroll.Domain.Constants;

/// <summary>GOV-002: stable decision identifiers bound to payroll rules/config fields.</summary>
public static class LegalDecisionIds
{
    public const string TaxBrackets = "LD-TAX-BRACKETS";
    public const string TaxExemption = "LD-TAX-EXEMPTION";
    public const string TaxableBase = "LD-TAXABLE-BASE";
    public const string EosFormula = "LD-EOS-FORMULA";
    public const string DailyDivisor = "LD-DAILY-DIVISOR";
    public const string ProrationBasis = "LD-PRORATION-BASIS";
    public const string NonWorkingDays = "LD-NON-WORKING-DAYS";
    public const string OfficialHolidays = "LD-OFFICIAL-HOLIDAYS";
    public const string MissionAllowance = "LD-MISSION-ALLOWANCE";
    public const string SecondmentPercent = "LD-SECONDMENT-PERCENT";
    public const string TrainingPremium = "LD-TRAINING-PREMIUM";
    public const string LeavePayoutDivisor = "LD-LEAVE-PAYOUT-DIVISOR";
    public const string RetroMonths = "LD-RETRO-MONTHS";
    public const string Rounding = "LD-ROUNDING";
    public const string Currency = "LD-CURRENCY";
    public const string SingleRegime = "LD-SINGLE-REGIME";
    public const string CivilServicePending = "LD-CIVIL-SERVICE-PENDING";

    /// <summary>Law50 interim decisions required for LegalAttest before Publish.</summary>
    public static readonly IReadOnlyList<string> Law50AttestSet =
    [
        TaxBrackets, TaxExemption, TaxableBase, EosFormula, DailyDivisor, ProrationBasis,
        NonWorkingDays, OfficialHolidays, MissionAllowance, SecondmentPercent, TrainingPremium,
        LeavePayoutDivisor, RetroMonths, Rounding, Currency, SingleRegime
    ];
}
