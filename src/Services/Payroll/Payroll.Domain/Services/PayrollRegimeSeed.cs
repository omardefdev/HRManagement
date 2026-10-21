using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>Builds default payroll configurations for supported legal regimes.</summary>
public static class PayrollRegimeSeed
{
    private const decimal TopTaxBracketUpperLimit = 9_999_999_999_999_999.99m;

    public static readonly IReadOnlyList<PayrollEngine.TaxBracketEntry> Law50TaxBrackets =
    [
        new(5_000m, 0.00m),
        new(15_000m, 0.05m),
        new(30_000m, 0.10m),
        new(50_000m, 0.15m),
        new(TopTaxBracketUpperLimit, 0.20m),
    ];

    public static readonly IReadOnlyList<PayrollEngine.TaxBracketEntry> CivilServiceTaxBrackets =
    [
        new(6_000m, 0.00m),
        new(18_000m, 0.05m),
        new(35_000m, 0.10m),
        new(55_000m, 0.15m),
        new(TopTaxBracketUpperLimit, 0.22m),
    ];

    public static PayrollConfiguration CreateLaw50Configuration(bool isActive = true) =>
        CreateConfiguration(
            code: "LAW_50_2004",
            regimeCode: LegalRegimeCodes.Law50_2004,
            nameAr: LegalRegimeCodes.DisplayNameAr(LegalRegimeCodes.Law50_2004),
            isActive: isActive,
            brackets: Law50TaxBrackets);

    public static PayrollConfiguration CreateLaw53Configuration(bool isActive = false) =>
        CreateConfiguration(
            code: "LAW_53_2021",
            regimeCode: LegalRegimeCodes.Law53_2021,
            nameAr: LegalRegimeCodes.DisplayNameAr(LegalRegimeCodes.Law53_2021),
            isActive: isActive,
            brackets: Law50TaxBrackets);

    public static PayrollConfiguration CreateCivilServiceConfiguration(bool isActive = false) =>
        CreateConfiguration(
            code: "CIVIL_SERVICE_2026",
            regimeCode: LegalRegimeCodes.CivilService2026,
            nameAr: LegalRegimeCodes.DisplayNameAr(LegalRegimeCodes.CivilService2026),
            isActive: isActive,
            brackets: CivilServiceTaxBrackets,
            missionAllowance: 0,
            secondmentPercent: 0);

    public static IEnumerable<OfficialHoliday> CreateDefaultHolidays(Guid? payrollConfigurationId = null)
    {
        var rows = new (int month, int day, string name)[]
        {
            (1, 1, "رأس السنة"),
            (4, 17, "عيد الجلاء"),
            (5, 1, "عيد العمال"),
            (10, 6, "ذكرى حرب أكتوبر"),
        };

        return rows.Select(r => new OfficialHoliday
        {
            Date = new DateOnly(2000, r.month, r.day),
            NameAr = r.name,
            IsRecurring = true,
            PayrollConfigurationId = payrollConfigurationId
        });
    }

    private static PayrollConfiguration CreateConfiguration(
        string code,
        string regimeCode,
        string nameAr,
        bool isActive,
        IReadOnlyList<PayrollEngine.TaxBracketEntry> brackets,
        decimal missionAllowance = 0,
        decimal secondmentPercent = 0)
    {
        var config = new PayrollConfiguration
        {
            Code = code,
            RegimeCode = regimeCode,
            NameAr = nameAr,
            IsActive = isActive,
            VersionNumber = 1,
            PublicationStatus = PayrollConfigPublicationStatus.Published,
            EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PublishedAt = DateTime.UtcNow,
            ApprovedAt = DateTime.UtcNow,
            ProrationDaysBasis = ProrationDaysBasis.WorkingDays,
            DailyRateDivisor = 30,
            MaxRetroCorrectionMonths = 12,
            EosGratuityRateFirstPeriod = 0.5m,
            EosGratuityFirstYearsThreshold = 5,
            EosGratuityRateAfterThreshold = 1.0m,
            NonWorkingDays = "Friday,Saturday",
            UseDbHolidaysOnly = true,
            TaxableBaseMode = "GrossPay",
            TaxExemptionAmount = 0,
            LeavePayoutDailyDivisor = 30,
            MissionAllowanceAmount = missionAllowance,
            SecondmentAllowancePercent = secondmentPercent,
            RoundingMode = PayrollRoundingMode.AwayFromZero,
            DecimalPlaces = 2,
            CurrencyCode = "SYP",
        };

        var i = 0;
        foreach (var b in brackets)
        {
            config.TaxBrackets.Add(new TaxBracket
            {
                UpperLimit = b.UpperLimit,
                RatePercent = b.Rate * 100m,
                SortOrder = i++
            });
        }

        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        return config;
    }
}
