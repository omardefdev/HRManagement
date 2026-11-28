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

    // ── PLAN-60 (§4): how the item is completed, and the two-person exception.

    [Display(Name = "نمط الإكمال")]
    public SeparationStepCompletionMode CompletionMode { get; set; } = SeparationStepCompletionMode.Manual;

    /// <summary>The saga party whose acknowledgement completes a <see cref="SeparationStepCompletionMode.PartyAck"/> item.</summary>
    [MaxLength(64)]
    public string? PartyName { get; set; }

    /// <summary>An uploaded file given as the evidence of a manual item.</summary>
    public Guid? EvidenceFileId { get; set; }

    public Guid? ExceptionRequestedBy { get; set; }

    public DateTime? ExceptionRequestedAt { get; set; }

    [MaxLength(1000)]
    public string? ExceptionReason { get; set; }

    public Guid? ExceptionApprovedBy { get; set; }

    public DateTime? ExceptionApprovedAt { get; set; }

    /// <summary>A second person approved an exception: the item counts as done for the execution, without being completed.</summary>
    public bool IsExcepted => ExceptionApprovedAt is not null;

    public virtual SeparationRecord? SeparationRecord { get; set; }
}
