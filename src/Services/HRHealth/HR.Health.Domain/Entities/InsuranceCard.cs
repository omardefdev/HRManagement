using System.ComponentModel.DataAnnotations;
using HR.Health.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "بطاقة التأمين الصحي")]
public class InsuranceCard : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "رقم العضوية")]
    public string MemberNumber { get; set; } = null!;

    [Display(Name = "شركة التأمين")]
    public string? Provider { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    public DateOnly IssueDate { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    public DateOnly ExpiryDate { get; set; }

    [Display(Name = "أفراد الأسرة")]
    public string? FamilyMembers { get; set; }

    [Display(Name = "عقد التأمين")]
    public Guid? InsuranceContractId { get; set; }

    public InsuranceContract? InsuranceContract { get; set; }

    [Display(Name = "الحالة")]
    public InsuranceCardStatus Status { get; set; } = InsuranceCardStatus.Draft;

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }
}
