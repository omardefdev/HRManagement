using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Movements.Domain.Entities;

[Display(Name = "طلب النقل")]
public class TransferRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "الوحدة المصدر")]
    public Guid FromOrgUnitId { get; set; }

    [Required]
    [Display(Name = "الوحدة المقصد")]
    public Guid ToOrgUnitId { get; set; }

    [Display(Name = "طالب النقل")]
    public Guid? RequestedBy { get; set; }

    [Required]
    [Display(Name = "تاريخ الطلب")]
    public DateOnly RequestDate { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; }

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "تاريخ الموافقة")]
    public DateOnly? ApprovedDate { get; set; }

    /// <summary>FUN-003: planned effective date — saga must not start before this day.</summary>
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public ICollection<MovementExecutionRecord> ExecutionRecords { get; set; } = [];
    public ICollection<TransferApproval> Approvals { get; set; } = [];
    public ICollection<TransferFinancialImpact> FinancialImpacts { get; set; } = [];
}
