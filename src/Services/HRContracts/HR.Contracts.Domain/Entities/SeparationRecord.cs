using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

/// <summary>
/// A separation (end of service). PLAN-60 (G-SEP-04): the record has an explicit state on the server. Its status, and every field a
/// transition writes, change only through <see cref="Services.SeparationWorkflowRules"/>: their setters are internal to the domain.
/// </summary>
[Display(Name = "سجل إنهاء الخدمة")]
public class SeparationRecord : AuditableEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "العقد")]
    [ForeignKey(nameof(Contract))]
    public Guid? ContractId { get; set; }

    [Display(Name = "نوع الإنهاء")]
    public SeparationType SeparationType { get; set; }

    [Display(Name = "تاريخ النفاذ")]
    [Required]
    public DateOnly EffectiveDate { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    /// <summary>History only (PLAN-60): before the engine it held the creator. No code writes it any more.</summary>
    [Display(Name = "المعتمد (سجل قديم)")]
    public Guid? ApprovedBy { get; private set; }

    [Display(Name = "المستندات")]
    public string? Documents { get; set; }

    /// <summary>
    /// The parties were told (the saga started). PLAN-60: derived from <see cref="Status"/> — true from <see cref="SeparationStatus.Executing"/>
    /// on. The column stays for the readers that use it.
    /// </summary>
    public bool SeparationEventPublished { get; internal set; }

    /// <summary>SYR-009: snapshot of HRCore service regime at separation submit.</summary>
    [MaxLength(50)]
    public string? SourceLegalRegimeId { get; set; }

    /// <summary>SYR-009: mapped payroll regime code for EOS bridge.</summary>
    [MaxLength(32)]
    public string? PayrollRegimeCode { get; set; }

    /// <summary>FUN-002: soft-cancel — history retained (no hard delete). PLAN-60: true exactly when the status is Cancelled.</summary>
    public bool IsCancelled { get; internal set; }

    public DateTime? CancelledAt { get; internal set; }

    [MaxLength(500)]
    public string? CancellationReason { get; internal set; }

    // ── PLAN-60 (§1): the state machine.

    [Display(Name = "الحالة")]
    public SeparationStatus Status { get; internal set; } = SeparationStatus.Draft;

    /// <summary>The HR.Core approval instance of the current (or last) submission.</summary>
    public Guid? WorkflowInstanceId { get; internal set; }

    [MaxLength(64)]
    public string? WorkflowCode { get; internal set; }

    /// <summary>The submission's number: the idempotency key is <c>SeparationRecord:{Id}:{SubmissionNo}</c>.</summary>
    public int SubmissionNo { get; set; }

    public Guid? SubmittedBy { get; internal set; }

    public DateTime? SubmittedAt { get; internal set; }

    public Guid? DecidedBy { get; internal set; }

    public DateTime? DecidedAt { get; internal set; }

    [MaxLength(1000)]
    public string? RejectionReason { get; internal set; }

    /// <summary>The numbered administrative decision (<c>DEC-SEP-YYYY-#####</c>).</summary>
    public Guid? DecisionId { get; set; }

    public Guid? ExecutedBy { get; internal set; }

    public DateTime? ExecutedAt { get; internal set; }

    public DateTime? CompletedAt { get; internal set; }

    /// <summary>
    /// PLAN-60 (P-5, rule 3): a record written before the state machine that the backfill set to Draft. The list shows «سجل قديم — يتطلب
    /// اعتماداً» until its first submission.
    /// </summary>
    public bool IsLegacyDraft { get; internal set; }

    public Contract? Contract { get; set; }

    public AdministrativeDecision? Decision { get; set; }
}
