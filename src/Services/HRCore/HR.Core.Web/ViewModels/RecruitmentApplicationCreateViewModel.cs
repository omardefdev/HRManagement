using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

public class RecruitmentApplicationCreateViewModel
{
    [Required]
    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الرقم الوطني")]
    public string? NationalId { get; set; }

    [Required]
    [Display(Name = "الوظيفة المتقدم لها")]
    public string AppliedPosition { get; set; } = string.Empty;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}
