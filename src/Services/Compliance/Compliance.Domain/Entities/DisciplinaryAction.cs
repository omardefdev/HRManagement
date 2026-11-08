using System.ComponentModel.DataAnnotations;
using Compliance.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "إجراء تأديبي")]
public class DisciplinaryAction : AuditableEntity
{
    [Display(Name = "معرّف الموظف")]
    [Required(ErrorMessage = "معرّف الموظف مطلوب")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الإجراء")]
    public ActionType ActionType { get; set; }

    [Display(Name = "صادر من")]
    public Guid? IssuedBy { get; set; }

    [Display(Name = "تاريخ الإصدار")]
    public DateTime IssuedAt { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "حالة الاستئناف")]
    public AppealStatus AppealStatus { get; set; }

    [Display(Name = "الشكوى المصدر")]
    public Guid? ComplaintId { get; set; }

    public virtual Complaint? Complaint { get; set; }

    /// <summary>FUN-004: FILE-002 evidence attachment.</summary>
    public Guid? EvidenceFileId { get; set; }

    // PLAN-46: the approval in HR.Core's engine. None of these is ever bound from a form.

    [Display(Name = "حالة الاعتماد")]
    public DisciplinaryApprovalStatus ApprovalStatus { get; set; }

    /// <summary>The instance of the current submission. Never <c>PendingApproval</c> without it (rule 3).</summary>
    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>The number of the last submission that reached the engine: the key is <c>DisciplinaryAction:{Id}:{SubmissionNo}</c>.</summary>
    public int SubmissionNo { get; set; }

    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }

    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    /// <summary>When <c>IDisciplinaryActionTaken</c> was staged for payroll. Set once; never published again.</summary>
    public DateTime? OutcomePublishedAt { get; set; }

    /// <summary>An action published to payroll before this plan: approved by the migration, never published again.</summary>
    public bool ApprovedBeforeEngine { get; set; }
}
