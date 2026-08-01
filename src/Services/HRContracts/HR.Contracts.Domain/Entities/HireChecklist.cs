using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "قائمة تحقق التعيين")]
public class HireChecklist : BaseEntity
{
    [Display(Name = "معاملة الخدمة")]
    [Required]
    public Guid ServiceTransactionId { get; set; }

    [Display(Name = "الخطوة")]
    public HireStep Step { get; set; }

    [Display(Name = "مكتمل")]
    public bool IsCompleted { get; set; }

    [Display(Name = "تاريخ الإكمال")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>FUN-001: steward / owning role for this step.</summary>
    [MaxLength(64)]
    public string? OwnerRole { get; set; }

    /// <summary>FUN-001: target SLA hours from journey matrix.</summary>
    public int SlaHours { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    public Guid? CompletedBy { get; set; }

    [MaxLength(128)]
    public string? CompletedByName { get; set; }

    public virtual ServiceTransaction? ServiceTransaction { get; set; }
}
