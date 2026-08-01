using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "نوع الخصم")]
public class DeductionType : BaseEntity
{
    [Required, MaxLength(50)]
    [Display(Name = "الكود")]
    public string Code { get; set; } = null!;

    [Required, MaxLength(200)]
    [Display(Name = "الاسم بالعربية")]
    public string NameAr { get; set; } = null!;

    [Display(Name = "إلزامي")]
    public bool IsStatutory { get; set; }

    [Display(Name = "نسبة مئوية")]
    public decimal? Percentage { get; set; }

    [Display(Name = "مبلغ ثابت")]
    public decimal? FixedAmount { get; set; }
}
