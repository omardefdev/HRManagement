using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-026: recorded outcome of a completed interview.</summary>
public enum InterviewOutcome
{
    [Display(Name = "حضر")]
    Attended = 0,
    [Display(Name = "لم يحضر")]
    NoShow = 1
}
