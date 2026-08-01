using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "نوع التعويض")]
public class CompensationType : AuditableEntity
{
    [Required(ErrorMessage = "الرمز مطلوب")]
    [Display(Name = "الرمز")]
    public string Code { get; set; } = null!;

    [Required(ErrorMessage = "الاسم بالعربي مطلوب")]
    [Display(Name = "الاسم بالعربي")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "الاسم بالإنكليزي")]
    public string? NameEn { get; set; }

    [Display(Name = "صيغة الحساب")]
    public string? CalculationFormula { get; set; }
}
