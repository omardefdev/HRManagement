using System.Globalization;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-027 (E): offer terms consistency — grade from the funded position, salary within the Payroll grade
/// bounds, proposed start date window. A grade deviation or an unverified salary makes the offer an
/// exception offer (see <see cref="Entities.OfferLetter.IsExceptionOffer"/>).
/// </summary>
public static class OfferTermsRules
{
    public const int MinGradeDeviationReasonLength = 10;
    public const int MaxGradeDeviationReasonLength = 500;
    public const int MinSalaryJustificationLength = 20;
    public const int MaxSalaryJustificationLength = 1000;

    public const string MissingGradeMessage = "تعذّر تحديد درجة العرض: المنصب الممول بلا درجة ولم تُحدَّد درجة.";
    public const string StartDateInPastMessage = "تاريخ المباشرة المقترح لا يجوز أن يكون قبل اليوم.";

    public static readonly string GradeDeviationReasonMessage =
        $"اختلاف الدرجة عن درجة المنصب الممول يتطلب مبرراً لا يقل عن {MinGradeDeviationReasonLength} أحرف.";

    public static readonly string SalaryJustificationMessage =
        $"تعذّر التحقق من حدود الدرجة في الرواتب؛ يلزم مبرر للراتب لا يقل عن {MinSalaryJustificationLength} حرفاً.";

    public static string StartDateTooLateMessage(int maxStartDays) =>
        $"تاريخ المباشرة المقترح يجب ألا يتجاوز {maxStartDays} يوماً من اليوم.";

    public static string SalaryOutOfRangeMessage(string grade, decimal min, decimal max) =>
        $"الراتب المعروض خارج حدود الدرجة {grade}: {FormatAmount(min)}–{FormatAmount(max)}.";

    public static string GradeNotFoundMessage(string grade) =>
        $"الدرجة {grade} غير معرّفة في سلّم الرواتب الساري.";

    /// <summary>The offered grade, or the funded position's grade when none was entered.</summary>
    public static string? ResolveGrade(string? offeredGrade, string? positionGrade) =>
        string.IsNullOrWhiteSpace(offeredGrade) ? NullIfBlank(positionGrade) : offeredGrade.Trim();

    public static bool IsGradeDeviation(string grade, string? positionGrade) =>
        !string.IsNullOrWhiteSpace(positionGrade)
        && !string.Equals(grade, positionGrade.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IsSalaryInRange(decimal salary, decimal min, decimal max) =>
        salary >= min && salary <= max;

    /// <summary>
    /// <paramref name="proposedStartDate"/> must fall between today (UTC) and today + <paramref name="maxStartDays"/>.
    /// </summary>
    public static string? ValidateStartDate(DateTime proposedStartDate, DateOnly todayUtc, int maxStartDays)
    {
        var start = DateOnly.FromDateTime(proposedStartDate);
        if (start < todayUtc)
            return StartDateInPastMessage;
        if (start > todayUtc.AddDays(maxStartDays))
            return StartDateTooLateMessage(maxStartDays);
        return null;
    }

    public static string FormatAmount(decimal amount) =>
        amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
