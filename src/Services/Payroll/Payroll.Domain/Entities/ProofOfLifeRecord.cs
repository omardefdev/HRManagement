using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Constants;

namespace Payroll.Domain.Entities;

[Display(Name = "سجل إثبات الحياة")]
public class ProofOfLifeRecord : AuditableEntity
{
    [Required(ErrorMessage = "الموظف مطلوب")]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تاريخ التحقق")]
    public DateTime VerificationDate { get; set; }

    [Required]
    [Display(Name = "تاريخ الانتهاء")]
    public DateTime ExpiryDate { get; set; }

    [Required]
    [Display(Name = "طريقة التحقق")]
    [MaxLength(64)]
    public string Method { get; set; } = "";

    [Required]
    [Display(Name = "الحالة")]
    [MaxLength(32)]
    public string Status { get; set; } = ProofOfLifeStatus.Valid;

    [Display(Name = "سبب الاستثناء")]
    [MaxLength(2000)]
    public string? ExceptionReason { get; set; }

    [Display(Name = "تحقق بواسطة")]
    public Guid? VerifiedBy { get; set; }
}
