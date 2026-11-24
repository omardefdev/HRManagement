using System.Globalization;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-54: the Arabic messages of a framework version's cycle. A refusal says what is required and changes nothing. Dates are
/// written with the invariant culture, so the text is the same whatever the request's culture.
/// </summary>
public static class PayrollFrameworkMessages
{
    public const string NotFound = "نسخة الإطار القانوني غير موجودة";
    public const string PublishNeedsApproved = "لا يُنشر إلا إصدار معتمد: المسودة تُعتمد ثم تُصادَق قانونياً ثم تُنشر";
    public const string NoFingerprint = "النسخة بلا بصمة محتوى؛ اعتمدها أولاً";
    public const string NoBrackets = "لا يُنشر إطار بلا شرائح ضريبة: استورد الشرائح في المسودة";
    public const string ActivateNeedsPublished = "التفعيل للنسخ المنشورة فقط؛ النشر لا يتم من هنا";
    public const string ApproveNeedsDraft = "لا يُعتمد إلا مسودة";
    public const string AttestNeedsApproved = "التصديق القانوني لنسخة معتمدة لم تُنشر بعد: اعتمد المسودة أولاً";
    public const string DraftOnly = "لا يُعدَّل إلا مسودة؛ النسخة المعتمدة أو المنشورة تُستنسخ في مسودة جديدة";
    public const string EffectiveFromRequired = "تاريخ السريان إلزامي";

    /// <summary>The page where a legal pack is imported (the first step of a production agency's first calculation).</summary>
    public const string ImportPath = "/PayrollConfigurations/Import";

    /// <summary>The calculation's refusal when no version governs the period: it says where a framework comes from.</summary>
    public const string NoFrameworkForPeriod =
        "لا يوجد إطار قانوني منشور يحكم هذه الفترة. استورد حزمة قانونية من «الرواتب ← الأطر القانونية ← استيراد حزمة قانونية» ("
        + ImportPath + ")، ثم يعتمدها شخص ثانٍ ويصادق عليها ثالث وتُنشر.";

    public const string NoPublishedFramework = "لا يوجد إطار منشور";

    public static string Day(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string NotAfterPublished(DateTime publishedFrom) =>
        $"تاريخ سريان النسخة يجب أن يلي سريان النسخة المنشورة حالياً لهذا الرمز ({Day(publishedFrom)}): النشر لا يقصّ نسخة سابقة بأثر رجعي";

    public static string InsidePostedPeriod(DateTime effectiveFrom, DateTime postedPeriodEnd) =>
        $"لا تُنشر نسخة تسري من {Day(effectiveFrom)}: للنظام نفسه تشغيل رواتب مرحّل تنتهي فترته في {Day(postedPeriodEnd)}. "
        + "اختر تاريخ سريان بعد آخر فترة مرحّلة (الأثر الرجعي غير متاح)";
}
