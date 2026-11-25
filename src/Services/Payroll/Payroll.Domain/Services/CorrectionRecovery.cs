namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-56 (§1): the recovery of a correction's negative difference. When a correction line's parts fall below zero the line pays
/// nothing, and a deduction of this type is created for the shortfall, due on the first day of the period after the corrected one, so
/// that the next regular run takes it back instead of the difference becoming zero. The deduction names its line.
/// </summary>
public static class CorrectionRecovery
{
    /// <summary>The type of the deduction. An ordinary ad-hoc deduction to the engine: charged once, in the first run it is due in.</summary>
    public const string DeductionType = "CorrectionRecovery";

    private const string ReferencePrefix = "CorrectionRecovery:";

    /// <summary>The source reference of the recovery of <paramref name="salaryLineId"/>: one recovery per correction line.</summary>
    public static string SourceReferenceOf(Guid salaryLineId) => ReferencePrefix + salaryLineId.ToString("N");

    /// <summary>The first day of the period after the corrected one: when the recovery is due.</summary>
    public static DateOnly DueDateAfter(DateTime correctedPeriodEnd) => DateOnly.FromDateTime(correctedPeriodEnd).AddDays(1);
}
