using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// PAY-002: canonical SHA-256 of a framework version's legal content.
/// <para>
/// <b>v1</b> (every version older than PLAN-54, and the Demo and Acceptance seeds): the code, the regime, the numeric fields and the tax
/// brackets. Its text is frozen: a v1 version is never hashed in another way, so the fingerprints stored on existing runs stay valid.
/// </para>
/// <para>
/// <b>v2</b> (PLAN-54, <see cref="PayrollConfiguration.HashSchemaVersion"/> = 2): v1's content, the effective date, and the four tables
/// inside the version (tax brackets, statutory deductions, holidays, disbursement priority rules) with the source of every numeric
/// field, each value with its legal reference, status and interim exception. Rows are sorted by their own keys and money is written
/// with two decimals in the invariant culture, so the fingerprint is the same in memory and after a reload from the database.
/// The record of the version's editors is not content and is not hashed.
/// </para>
/// </summary>
public static class PayrollConfigurationHashService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ComputeContentHash(PayrollConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.IsLegacyFramework ? ComputeV1(config) : ComputeV2(config);
    }

    /// <summary>The v1 fingerprint, exactly as it was before PLAN-54. Do not change this method.</summary>
    private static string ComputeV1(PayrollConfiguration config)
    {
        var brackets = (config.TaxBrackets ?? Array.Empty<TaxBracket>())
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.UpperLimit)
            .Select(b => new
            {
                b.SortOrder,
                UpperLimit = b.UpperLimit,
                RatePercent = b.RatePercent
            })
            .ToList();

        var payload = new
        {
            config.Code,
            config.RegimeCode,
            config.ProrationDaysBasis,
            config.DailyRateDivisor,
            config.MissionAllowanceAmount,
            config.SecondmentAllowancePercent,
            config.EnableTrainingPremiums,
            config.TrainingPremiumAmount,
            config.MedicalCompensationDefaultAmount,
            config.MaxRetroCorrectionMonths,
            config.EosGratuityRateFirstPeriod,
            config.EosGratuityFirstYearsThreshold,
            config.EosGratuityRateAfterThreshold,
            config.NonWorkingDays,
            config.UseDbHolidaysOnly,
            config.TaxableBaseMode,
            config.TaxExemptionAmount,
            config.LeavePayoutDailyDivisor,
            config.RoundingMode,
            config.DecimalPlaces,
            config.CurrencyCode,
            TaxBrackets = brackets
        };

        return Sha256(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string ComputeV2(PayrollConfiguration config) => Sha256(BuildV2Canonical(config));

    /// <summary>The canonical text fingerprint v2 hashes (exposed for the tests that prove what it covers).</summary>
    public static string BuildV2Canonical(PayrollConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var payload = new
        {
            Schema = PayrollConfiguration.CurrentHashSchemaVersion,
            config.Code,
            config.RegimeCode,
            EffectiveFrom = config.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Fields = new
            {
                ProrationDaysBasis = config.ProrationDaysBasis.ToString(),
                config.DailyRateDivisor,
                MissionAllowanceAmount = Money(config.MissionAllowanceAmount),
                SecondmentAllowancePercent = Money(config.SecondmentAllowancePercent),
                config.EnableTrainingPremiums,
                TrainingPremiumAmount = Money(config.TrainingPremiumAmount),
                MedicalCompensationDefaultAmount = Money(config.MedicalCompensationDefaultAmount),
                config.MaxRetroCorrectionMonths,
                EosGratuityRateFirstPeriod = Money(config.EosGratuityRateFirstPeriod),
                config.EosGratuityFirstYearsThreshold,
                EosGratuityRateAfterThreshold = Money(config.EosGratuityRateAfterThreshold),
                config.NonWorkingDays,
                config.UseDbHolidaysOnly,
                config.TaxableBaseMode,
                TaxExemptionAmount = Money(config.TaxExemptionAmount),
                config.LeavePayoutDailyDivisor,
                RoundingMode = config.RoundingMode.ToString(),
                config.DecimalPlaces,
                config.CurrencyCode
            },
            FieldSources = (config.FieldSources ?? [])
                .OrderBy(f => f.FieldName, StringComparer.Ordinal)
                .Select(f => new { f.FieldName, Source = Source(f) })
                .ToList(),
            TaxBrackets = (config.TaxBrackets ?? [])
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.UpperLimit)
                .Select(b => new
                {
                    b.SortOrder,
                    UpperLimit = Money(b.UpperLimit),
                    RatePercent = Money(b.RatePercent),
                    Source = Source(b)
                })
                .ToList(),
            StatutoryDeductions = (config.StatutoryDeductions ?? [])
                .OrderBy(d => d.SortOrder)
                .ThenBy(d => d.Code, StringComparer.Ordinal)
                .Select(d => new
                {
                    d.SortOrder,
                    d.Code,
                    d.NameAr,
                    EmployeePercent = Money(d.EmployeePercent),
                    EmployerPercent = Money(d.EmployerPercent),
                    FixedAmount = Money(d.FixedAmount),
                    BaseKind = d.BaseKind.ToString(),
                    Ceiling = d.Ceiling is decimal ceiling ? Money(ceiling) : null,
                    d.InsuranceBody,
                    Source = Source(d)
                })
                .ToList(),
            Holidays = (config.Holidays ?? [])
                .OrderBy(h => h.Date)
                .ThenBy(h => h.NameAr, StringComparer.Ordinal)
                .Select(h => new
                {
                    Date = h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    h.NameAr,
                    h.IsRecurring,
                    Source = Source(h)
                })
                .ToList(),
            PriorityRules = (config.PriorityRules ?? [])
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Category, StringComparer.Ordinal)
                .Select(r => new
                {
                    r.Priority,
                    r.Category,
                    MinimumAmount = Money(r.MinimumAmount),
                    Source = Source(r)
                })
                .ToList()
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object Source(ILegallySourcedValue value) => new
    {
        LegalReference = string.IsNullOrWhiteSpace(value.LegalReference) ? null : value.LegalReference.Trim(),
        LegalStatus = value.LegalStatus.ToString(),
        InterimExceptionId = string.IsNullOrWhiteSpace(value.InterimExceptionId) ? null : value.InterimExceptionId.Trim()
    };

    /// <summary>Money and percentages are stored with two decimals: 5, 5.0 and 5.00 are one value and one text.</summary>
    private static string Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Sha256(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
