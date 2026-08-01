using System.Globalization;
using System.Text.RegularExpressions;

namespace Payroll.Domain.Services;

public static partial class MedicalCompensationRules
{
    public static bool IsCompensationRecommendation(string recommendationType) =>
        string.Equals(recommendationType, "Compensation", StringComparison.OrdinalIgnoreCase);

    public static decimal ResolveAmount(string? details, decimal defaultAmount)
    {
        if (string.IsNullOrWhiteSpace(details))
            return defaultAmount;

        var match = AmountPattern().Match(details);
        if (match.Success
            && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return defaultAmount;
    }

    public static string BuildAdjustmentReason(Guid recommendationId, string details) =>
        $"تعويض طبي — {details} — MedicalRecommendation-{recommendationId}";

    [GeneratedRegex(@"(?<!\d)(\d+(?:\.\d+)?)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();
}
