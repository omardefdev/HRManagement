using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Movements.Domain.Entities;

[Display(Name = "طلب الترفيع")]
public class PromotionRequest : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "نظام الخدمة")]
    [MaxLength(50)]
    public string? SourceLegalRegimeId { get; set; }

    [Display(Name = "من درجة")]
    public int FromGrade { get; set; }

    [Display(Name = "إلى درجة")]
    public int ToGrade { get; set; }

    [Display(Name = "من مرتبة")]
    public int FromStep { get; set; }

    [Display(Name = "إلى مرتبة")]
    public int ToStep { get; set; }

    [Required(ErrorMessage = "مرجع القرار مطلوب")]
    [Display(Name = "مرجع القرار")]
    [MaxLength(200)]
    public string TransitionDecisionRef { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    [Display(Name = "الحالة")]
    public PromotionRequestStatus Status { get; set; }

    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public Guid? RequestedBy { get; set; }

    public ICollection<MovementExecutionRecord> ExecutionRecords { get; set; } = [];
}
