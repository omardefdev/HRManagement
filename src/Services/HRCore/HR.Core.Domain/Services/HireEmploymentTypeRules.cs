using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-029 (B): the hire "employment category" is a domain <see cref="EmploymentType"/> (Requariments.md:
/// دائم – إداري – متعاقد مؤقت). The form posts enum names; the legacy codes still posted by older clients and tests
/// are mapped. There is no internship value in the domain, so INTERNSHIP is refused instead of being invented.
/// </summary>
public static class HireEmploymentTypeRules
{
    public const int MinChangeReasonLength = 10;

    public const string InvalidMessage = "يرجى اختيار فئة توظيف صحيحة.";

    public const string InternshipNotSupportedMessage =
        "فئة «تدريب عملي» غير معرّفة في أنواع التوظيف المعتمدة — اختر: دائم أو إداري أو متعاقد مؤقت.";

    public const string ChangeReasonRequiredMessage =
        "اختيار فئة توظيف غير الافتراضية يتطلب مبرراً (10 أحرف على الأقل).";

    /// <summary>Audit action recorded on the application when a non-default type was justified.</summary>
    public const string ChangeAuditAction = "HireEmploymentTypeChange";

    public const string LegacyInternshipCode = "INTERNSHIP";

    /// <summary>Hire form options, in display order (value = enum name).</summary>
    public static IReadOnlyList<(EmploymentType Type, string Label)> Categories { get; } =
    [
        (EmploymentType.Permanent, "دائم"),
        (EmploymentType.Administrative, "إداري"),
        (EmploymentType.Temporary, "متعاقد مؤقت")
    ];

    /// <summary>Pre-PLAN-29 form codes → domain values (INTERNSHIP has none).</summary>
    public static IReadOnlyDictionary<string, EmploymentType> LegacyCodes { get; } =
        new Dictionary<string, EmploymentType>(StringComparer.OrdinalIgnoreCase)
        {
            ["PERMANENT"] = EmploymentType.Permanent,
            ["TEMPORARY"] = EmploymentType.Temporary,
            ["CONTRACT"] = EmploymentType.Temporary
        };

    public static string LabelOf(EmploymentType type) =>
        Categories.FirstOrDefault(c => c.Type == type).Label ?? type.ToString();

    /// <summary>Resolves an enum name (case-insensitive) or a legacy code; numbers and unknown codes are refused.</summary>
    public static bool TryResolve(string? code, out EmploymentType type, out string? error)
    {
        type = default;
        error = null;
        var value = code?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            error = InvalidMessage;
            return false;
        }

        if (string.Equals(value, LegacyInternshipCode, StringComparison.OrdinalIgnoreCase))
        {
            error = InternshipNotSupportedMessage;
            return false;
        }

        if (LegacyCodes.TryGetValue(value, out type))
            return true;

        if (!char.IsDigit(value[0]) && value[0] != '-'
            && Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type))
            return true;

        type = default;
        error = InvalidMessage;
        return false;
    }

    public static bool RequiresChangeReason(EmploymentType chosen, EmploymentType defaultType) => chosen != defaultType;

    public static bool IsValidChangeReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= MinChangeReasonLength;
}
