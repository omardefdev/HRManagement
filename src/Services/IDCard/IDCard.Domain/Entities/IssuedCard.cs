using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using IDCard.Domain.Enums;

namespace IDCard.Domain.Entities;

[Display(Name = "بطاقة مُصدرة")]
public class IssuedCard : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "التصميم")]
    public Guid CardDesignId { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssuedAt { get; set; }

    [Display(Name = "تاريخ الانتهاء")]
    public DateOnly? ExpiresAt { get; set; }

    [Display(Name = "الحالة")]
    public CardStatus Status { get; set; }

    [Required]
    [Display(Name = "الرقم التسلسلي")]
    public string SerialNumber { get; set; } = null!;

    [Display(Name = "سجل التسليم")]
    public Guid? DeliveryRecordId { get; set; }

    public CardDesign CardDesign { get; set; } = null!;
    public CardDeliveryRecord? DeliveryRecord { get; set; }
    public ICollection<AccessPermission> AccessPermissions { get; set; } = new List<AccessPermission>();

    public DateTime? RevokedAt { get; set; }
    public int RevokeSlaHours { get; set; } = 48;
    public bool UrgentRevoke { get; set; }
    public Guid? ReplacesCardId { get; set; }
}
