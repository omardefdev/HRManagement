using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

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

    [Display(Name = "المعتمد")]
    public Guid? ApprovedBy { get; set; }

    [Display(Name = "المستندات")]
    public string? Documents { get; set; }

    /// <summary>تم نشر حدث إكمال الفصل إلى الأنظمة الأخرى (رواتب، أرشيف، إلخ).</summary>
    public bool SeparationEventPublished { get; set; }

    /// <summary>SYR-009: snapshot of HRCore service regime at separation submit.</summary>
    [MaxLength(50)]
    public string? SourceLegalRegimeId { get; set; }

    /// <summary>SYR-009: mapped payroll regime code for EOS bridge.</summary>
    [MaxLength(32)]
    public string? PayrollRegimeCode { get; set; }

    /// <summary>FUN-002: soft-cancel — history retained (no hard delete).</summary>
    public bool IsCancelled { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public Contract? Contract { get; set; }
}
