using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "طلب الندب")]
public class DelegationRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "الجهة المستضيفة مطلوبة")]
    [Display(Name = "الجهة المستضيفة")]
    [MaxLength(256)]
    public string HostOrg { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الحالة")]
    public DelegationRequestStatus Status { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public Guid? RequestedBy { get; set; }

    public ICollection<MovementExecutionRecord> ExecutionRecords { get; set; } = [];
}
