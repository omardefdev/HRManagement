using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>Lifecycle action subject to Separation of Duties (IAM-003).</summary>
public enum SodAction
{
    [Display(Name = "إجراء")]
    Make,
    [Display(Name = "تحقق")]
    Check,
    [Display(Name = "ترحيل")]
    Post,
    [Display(Name = "عكس")]
    Reverse
}
