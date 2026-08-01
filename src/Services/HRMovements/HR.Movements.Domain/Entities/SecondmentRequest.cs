using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "طلب الانتداب")]
public class SecondmentRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "الجهة المستضيفة مطلوبة")]
    [Display(Name = "الجهة المستضيفة")]
    public string HostOrg { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الحالة")]
    public SecondmentStatus Status { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    /// <summary>FUN-003: soft-cancel retains history.</summary>
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    /// <summary>Creator for SoD (FUN-003).</summary>
    public Guid? RequestedBy { get; set; }

    public ICollection<MovementExecutionRecord> ExecutionRecords { get; set; } = [];
}
