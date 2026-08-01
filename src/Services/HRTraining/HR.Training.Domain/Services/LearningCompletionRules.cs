using HR.Training.Domain.Enums;

namespace HR.Training.Domain.Services;

/// <summary>
/// قواعد تقدّم التعلّم الإلكتروني وإصدار الشهادة. دوال صرفة قابلة للاختبار.
/// </summary>
public static class LearningCompletionRules
{
    public const int CompletionThresholdPercent = 100;

    public static bool IsComplete(int progressPercent) => progressPercent >= CompletionThresholdPercent;

    public static LearningEnrollmentStatus StatusFor(int progressPercent)
    {
        if (progressPercent <= 0) return LearningEnrollmentStatus.Enrolled;
        return IsComplete(progressPercent) ? LearningEnrollmentStatus.Completed : LearningEnrollmentStatus.InProgress;
    }

    /// <summary>يقصُر النسبة على المجال 0..100.</summary>
    public static int ClampProgress(int progressPercent) => Math.Clamp(progressPercent, 0, 100);

    /// <summary>رقم شهادة فريد قابل للتكرار حتمياً بناءً على الدورة والموظف وتاريخ الإكمال.</summary>
    public static string GenerateCertificateNumber(string courseCode, Guid employeeId, DateOnly completedOn)
    {
        var code = string.IsNullOrWhiteSpace(courseCode) ? "CRS" : courseCode.Trim().ToUpperInvariant();
        var emp = employeeId.ToString("N")[..6].ToUpperInvariant();
        return $"CERT-{code}-{completedOn:yyyyMMdd}-{emp}";
    }
}
