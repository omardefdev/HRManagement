using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "ملف راتب الموظف")]
public class EmployeePayProfile : BaseEntity
{
    /// <summary>The message of the three obsolete bank properties (PLAN-55).</summary>
    public const string BankFieldsObsolete =
        "PLAN-55: the employee's bank account is EmployeeBankAccount (registered by one person, verified by another). This column stays for a rollback only.";

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
    [DataType(DataType.Date)]
    public DateTime EffectiveFrom { get; set; }

    [Display(Name = "ساري إلى")]
    public DateTime? EffectiveTo { get; set; }

    [Display(Name = "فعّال")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    /// <summary>
    /// The account number an older build kept on the profile. PLAN-55: the employee's bank account is <see cref="EmployeeBankAccount"/>,
    /// registered by one person and verified by another; migration PAY055 moved any number found here to a pending account. Nothing
    /// reads or writes this property, and a new version does not carry it. The column stays: the previous tag reads it after a rollback.
    /// </summary>
    [MaxLength(34)]
    [AuditRedact]
    [Display(Name = "الحساب المصرفي (IBAN)")]
    [Obsolete(BankFieldsObsolete)]
    public string? BankAccountIban { get; set; }

    /// <summary>See <see cref="BankAccountIban"/>: replaced by <see cref="EmployeeBankAccount.BankNameAr"/>.</summary>
    [MaxLength(120)]
    [Display(Name = "المصرف")]
    [Obsolete(BankFieldsObsolete)]
    public string? BankName { get; set; }

    /// <summary>SYR-007: payroll legal regime code mapped from HRCore service regime.</summary>
    [MaxLength(50)]
    [Display(Name = "رمز النظام القانوني")]
    public string? RegimeCode { get; set; }

    /// <summary>See <see cref="BankAccountIban"/>: replaced by <see cref="EmployeeBankAccount.VerifiedAt"/>.</summary>
    [Display(Name = "تاريخ التحقق من الحساب")]
    [Obsolete(BankFieldsObsolete)]
    public DateTime? BankAccountVerifiedAt { get; set; }

    public PayGrade? PayGrade { get; set; }
}
