using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "جدول الرواتب")]
public class SalaryScale : BaseEntity
{
    [Required, MaxLength(50)]
    [Display(Name = "الكود")]
    public string Code { get; set; } = null!;

    [Required, MaxLength(200)]
    [Display(Name = "الاسم بالعربية")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "ساري من")]
    public DateTime EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    public DateTime? EffectiveTo { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    public ICollection<PayGrade> PayGrades { get; set; } = new List<PayGrade>();
}
