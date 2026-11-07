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

    /// <summary>PLAN-46: the card a replacement request replaces; it is revoked when the new card is issued.</summary>
    public Guid? ReplacesCardId { get; set; }

    // PLAN-46: the approval in HR.Core's engine. None of these is ever bound from a form.

    /// <summary>The instance of the current submission. Never <c>PendingApproval</c> without it (rule 3).</summary>
    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>The number of the last submission that reached the engine: the key is <c>CardRequest:{Id}:{SubmissionNo}</c>.</summary>
    public int SubmissionNo { get; set; }

    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }
}
