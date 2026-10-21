using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public class RecruitmentApplicationCreateViewModel
{
    [Required]
    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الرقم الوطني")]
    public string? NationalId { get; set; }

    [Required(ErrorMessage = "يجب اختيار طلب شاغر منشور.")]
    [Display(Name = "طلب الشاغر")]
    public Guid RequisitionId { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public SelectList? PublishedRequisitions { get; set; }
}
