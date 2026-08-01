using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "قائمة تحقق الفصل")]
public class SeparationChecklist : BaseEntity
{
    [Display(Name = "سجل الفصل")]
    [Required]
    public Guid SeparationRecordId { get; set; }

    [Display(Name = "الخطوة")]
    public SeparationStep Step { get; set; }

    [Display(Name = "مكتمل")]
    public bool IsCompleted { get; set; }

    [Display(Name = "تاريخ الإكمال")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "أُكمل بواسطة")]
    public Guid? CompletedBy { get; set; }

    [Display(Name = "مرجع الدليل")]
    public string? EvidenceRef { get; set; }

    [Display(Name = "النظام المصدر")]
    public string? SourceSystem { get; set; }

    [Display(Name = "إلزامي")]
    public bool IsRequired { get; set; } = true;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>FUN-002: steward / owning role for this step.</summary>
    [MaxLength(64)]
    public string? OwnerRole { get; set; }

    /// <summary>FUN-002: target SLA hours from journey matrix.</summary>
    public int SlaHours { get; set; }

    [MaxLength(128)]
    public string? CompletedByName { get; set; }

    public virtual SeparationRecord? SeparationRecord { get; set; }
}
