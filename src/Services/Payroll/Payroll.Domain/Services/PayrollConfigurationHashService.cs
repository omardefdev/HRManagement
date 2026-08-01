using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>PAY-002: canonical SHA-256 of legal configuration content (rules + tax brackets).</summary>
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

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
