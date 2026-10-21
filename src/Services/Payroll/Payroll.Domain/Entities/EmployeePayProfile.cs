using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "ملف راتب الموظف")]
public class EmployeePayProfile : BaseEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الدرجة")]
    [ForeignKey(nameof(PayGrade))]
    public Guid? PayGradeId { get; set; }

    [Display(Name = "الراتب الأساسي")]
    [AuditRedact]
    public decimal BaseSalary { get; set; }

    [Display(Name = "ساري من")]
    public DateTime EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    public DateTime? EffectiveTo { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    /// <summary>
    /// Beneficiary account the salary is paid into, in IBAN form.
    /// <para>
    /// The disbursement file has always carried a BankAccount column and always left it empty,
    /// because no entity in the system stored one. A payment instruction without a destination
    /// account cannot reach the employee, so this is a precondition for any real transfer.
    /// </para>
    /// </summary>
    [MaxLength(34)]
    [AuditRedact]
    [Display(Name = "الحساب المصرفي (IBAN)")]
    public string? BankAccountIban { get; set; }

    [MaxLength(120)]
    [Display(Name = "المصرف")]
    public string? BankName { get; set; }

    /// <summary>SYR-007: payroll legal regime code mapped from HRCore service regime.</summary>
    [MaxLength(50)]
    [Display(Name = "رمز النظام القانوني")]
    public string? RegimeCode { get; set; }

    /// <summary>Set once the account has been confirmed with the bank; settlement requires it.</summary>
    [Display(Name = "تاريخ التحقق من الحساب")]
    public DateTime? BankAccountVerifiedAt { get; set; }

    public PayGrade? PayGrade { get; set; }
}
