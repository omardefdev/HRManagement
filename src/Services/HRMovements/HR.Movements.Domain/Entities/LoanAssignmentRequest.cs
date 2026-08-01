using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "طلب إعارة")]
public class LoanAssignmentRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "الوحدة المُعيرة")]
    public Guid LendingOrgUnitId { get; set; }

    [Required]
    [Display(Name = "الوحدة المستفيدة")]
    public Guid BorrowingOrgUnitId { get; set; }

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الحالة")]
    public LoanAssignmentStatus Status { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public Guid? RequestedBy { get; set; }

    public ICollection<MovementExecutionRecord> ExecutionRecords { get; set; } = [];
}
