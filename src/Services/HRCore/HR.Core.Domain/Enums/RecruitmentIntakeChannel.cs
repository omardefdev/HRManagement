using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>
/// HCM-030 (A5): how a recruitment application entered the selection path (stored as string(20)).
/// Used by the HR badges and, later, by PLAN-32 analytics.
/// </summary>
public enum RecruitmentIntakeChannel
{
    [Display(Name = "إدخال يدوي")]
    HrEntered = 0,
    [Display(Name = "بوابة عامة")]
    Public = 1,
    [Display(Name = "ترشّح داخلي")]
    Internal = 2,
    [Display(Name = "قديم")]
    Legacy = 3
}
