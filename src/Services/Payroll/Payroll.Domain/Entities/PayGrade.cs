using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "الدرجة الوظيفية")]
public class PayGrade : BaseEntity
{
    [Required]
    [Display(Name = "جدول الرواتب")]
    [ForeignKey(nameof(SalaryScale))]
    public Guid SalaryScaleId { get; set; }

    [Required, MaxLength(50)]
    [Display(Name = "كود الدرجة")]
    public string GradeCode { get; set; } = null!;

    [Required, MaxLength(200)]
    [Display(Name = "اسم الدرجة")]
    public string GradeNameAr { get; set; } = null!;

    [Display(Name = "الراتب الأساسي")]
    public decimal BaseSalary { get; set; }

    [Display(Name = "الحد الأدنى")]
    public decimal MinSalary { get; set; }

    [Display(Name = "الحد الأقصى")]
    public decimal MaxSalary { get; set; }

    public SalaryScale SalaryScale { get; set; } = null!;
}
