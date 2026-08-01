using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IDCard.Domain.Enums;

namespace IDCard.Domain.Entities;

[Display(Name = "طلب بطاقة")]
public class CardRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "طالب البطاقة")]
    public Guid? RequestedBy { get; set; }

    [Required]
    [Display(Name = "تاريخ الطلب")]
    public DateOnly RequestDate { get; set; }

    [Display(Name = "الغرض")]
    public CardRequestPurpose Purpose { get; set; }

    [Display(Name = "الحالة")]
    public CardRequestStatus Status { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public Guid? IssuedCardId { get; set; }
}
