using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// قواعد تصنيف جاهزية مرشّح التعاقب الوظيفي بناءً على آخر درجة تقييم وسنوات الخدمة والمؤهل.
/// دالة صرفة قابلة للاختبار.
/// </summary>
public static class SuccessionReadinessRules
{
    /// <summary>أدنى درجة تقييم لاعتبار المرشّح جاهزاً الآن.</summary>
    public const decimal ReadyNowScoreThreshold = 85m;

    /// <summary>أدنى سنوات خدمة لاعتبار المرشّح جاهزاً الآن.</summary>
    public const int ReadyNowMinYears = 3;

    /// <summary>أدنى درجة تقييم لاعتبار المرشّح جاهزاً خلال سنة.</summary>
    public const decimal ReadyWithinYearScoreThreshold = 70m;

    public static ReadinessLevel Classify(decimal latestEvaluationScore, int yearsInService, bool meetsQualification)
    {
        if (!meetsQualification)
            return ReadinessLevel.NeedsDevelopment;

        if (latestEvaluationScore >= ReadyNowScoreThreshold && yearsInService >= ReadyNowMinYears)
            return ReadinessLevel.ReadyNow;

        if (latestEvaluationScore >= ReadyWithinYearScoreThreshold)
            return ReadinessLevel.ReadyWithinYear;

        return ReadinessLevel.NeedsDevelopment;
    }
}
